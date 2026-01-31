// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using ktsu.Extensions;
using ktsu.Semantics.Strings;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

internal sealed class AzureDevOps : BuildProvider
{
	internal static BuildProviderName BuildProviderName => nameof(AzureDevOps).As<BuildProviderName>();
	internal override BuildProviderName Name => BuildProviderName;

	private VssConnection? Connection { get; set; }
	private ProjectHttpClient? ProjectClient { get; set; }
	private BuildHttpClient? BuildClient { get; set; }

	private void UpdateAzureDevOpsClientCredentials()
	{
		if (!string.IsNullOrEmpty(AccountId) && !string.IsNullOrEmpty(Token))
		{
			try
			{
				Uri collectionUri = new($"https://dev.azure.com/{AccountId}");
				VssBasicCredential credentials = new(string.Empty, Token);
				Connection = new(collectionUri, credentials);
				ProjectClient = Connection.GetClient<ProjectHttpClient>();
				BuildClient = Connection.GetClient<BuildHttpClient>();
			}
			catch (Exception)
			{
				Connection = null;
				ProjectClient = null;
				BuildClient = null;
			}
		}
	}

	internal override async Task UpdateRepositoriesAsync(Owner owner)
	{
		if (AccountId.IsEmpty() || Token.IsEmpty())
		{
			return;
		}

		UpdateAzureDevOpsClientCredentials();
		if (ProjectClient == null)
		{
			return;
		}

		await MakeAzureDevOpsRequestAsync($"{Name}/{owner.Name}", async () =>
		{
			// In Azure DevOps, projects are the top-level containers
			// The owner name represents a project in Azure DevOps
			IEnumerable<TeamProjectReference> projects = await ProjectClient.GetProjects().ConfigureAwait(false);

			foreach (TeamProjectReference? project in projects)
			{
				if (project.Name == owner.Name)
				{
					RepositoryId repositoryId = project.Id.ToString().As<RepositoryId>();
					RepositoryName repositoryName = project.Name.As<RepositoryName>();
					Repository repository = owner.CreateRepository(repositoryName, repositoryId);
					if (owner.Repositories.TryAdd(repositoryId, repository))
					{
						BuildMonitor.QueueSaveAppData();
					}
					break;
				}
			}
		}).ConfigureAwait(false);
	}

	internal override async Task UpdateBuildsAsync(Repository repository)
	{
		if (AccountId.IsEmpty() || Token.IsEmpty())
		{
			return;
		}

		UpdateAzureDevOpsClientCredentials();
		if (BuildClient == null)
		{
			return;
		}

		await MakeAzureDevOpsRequestAsync($"{Name}/{repository.Owner.Name}/{repository.Name}", async () =>
		{
			List<BuildDefinitionReference> definitions = await BuildClient.GetDefinitionsAsync(repository.Owner.Name).ConfigureAwait(false);
			foreach (BuildDefinitionReference? definition in definitions)
			{
				BuildName buildName = definition.Name.As<BuildName>();
				BuildId buildId = definition.Id.ToString(CultureInfo.InvariantCulture).As<BuildId>();
				Build build = repository.CreateBuild(buildName, buildId);
				if (repository.Builds.TryAdd(buildId, build))
				{
					BuildMonitor.QueueSaveAppData();
				}
			}
		}).ConfigureAwait(false);
	}

	internal override async Task UpdateBuildAsync(Build build)
	{
		if (AccountId.IsEmpty() || Token.IsEmpty())
		{
			return;
		}

		UpdateAzureDevOpsClientCredentials();
		if (BuildClient == null)
		{
			return;
		}

		await MakeAzureDevOpsRequestAsync($"{Name}/{build.Owner.Name}/{build.Repository.Name}/{build.Name}", async () =>
		{
			List<Microsoft.TeamFoundation.Build.WebApi.Build> builds = await BuildClient.GetBuildsAsync(
				build.Owner.Name,
				definitions: [int.Parse(build.Id, CultureInfo.InvariantCulture)],
				top: 10
			).ConfigureAwait(false);

			foreach (Microsoft.TeamFoundation.Build.WebApi.Build? azureBuild in builds)
			{
				RunId runId = azureBuild.Id.ToString(CultureInfo.InvariantCulture).As<RunId>();
				RunName runName = $"{azureBuild.BuildNumber}".As<RunName>();
				Run run = build.Runs.GetOrCreate(runId, build.CreateRun(runName, runId));
				UpdateRunFromBuild(run, azureBuild);
			}
		}).ConfigureAwait(false);
	}

	internal override async Task UpdateRunAsync(Run run)
	{
		if (AccountId.IsEmpty() || Token.IsEmpty())
		{
			return;
		}

		UpdateAzureDevOpsClientCredentials();
		if (BuildClient == null)
		{
			return;
		}

		await MakeAzureDevOpsRequestAsync($"{Name}/{run.Owner.Name}/{run.Repository.Name}/{run.Build.Name}/{run.Name}", async () =>
		{
			Microsoft.TeamFoundation.Build.WebApi.Build azureBuild = await BuildClient.GetBuildAsync(
				run.Owner.Name,
				int.Parse(run.Id, CultureInfo.InvariantCulture)
			).ConfigureAwait(false);
			UpdateRunFromBuild(run, azureBuild);
		}).ConfigureAwait(false);
	}

	private static void UpdateRunFromBuild(Run run, Microsoft.TeamFoundation.Build.WebApi.Build build)
	{
		// Use QueueTime as fallback for StartTime if not yet started
		run.Started = build.StartTime ?? build.QueueTime ?? DateTimeOffset.UtcNow;
		
		// For LastUpdated, use current time if not finished yet (in-progress builds)
		run.LastUpdated = build.FinishTime ?? DateTimeOffset.UtcNow;
		
		run.Branch = (build.SourceBranch ?? string.Empty).As<BranchName>();

		RunStatus previousStatus = run.Status;
		run.Status = build.Status switch
		{
			BuildStatus.NotStarted => RunStatus.Pending,
			BuildStatus.InProgress => RunStatus.Running,
			BuildStatus.Cancelling => RunStatus.Running,
			BuildStatus.Completed when build.Result == BuildResult.Succeeded => RunStatus.Success,
			BuildStatus.Completed when build.Result == BuildResult.PartiallySucceeded => RunStatus.Success,
			BuildStatus.Completed when build.Result == BuildResult.Failed => RunStatus.Failure,
			BuildStatus.Completed when build.Result == BuildResult.Canceled => RunStatus.Canceled,
			BuildStatus.Completed => RunStatus.Failure,
			_ => RunStatus.Pending,
		};

		// Clear errors if the run is no longer a failure
		if (run.Status != RunStatus.Failure && run.Errors.Count > 0)
		{
			run.Errors = [];
		}

		run.Build.UpdateFromRun(run);
		BuildMonitor.QueueSaveAppData();
	}

	internal async Task MakeAzureDevOpsRequestAsync(string name, Func<Task> action)
	{
		await RequestSemaphore.WaitAsync().ConfigureAwait(false);
		try
		{
			TimeSpan waitTime = GetRateLimitWaitTime();
			await Task.Delay(waitTime).ConfigureAwait(false);

			try
			{
				await BuildMonitor.MakeRequestAsync(name, action).ConfigureAwait(false);
				ClearStatus();
			}
			catch (VssServiceResponseException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized)
			{
				OnAuthenticationFailure();
			}
			catch (VssServiceResponseException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.TooManyRequests)
			{
				OnRateLimitExceeded();
			}
			catch (VssServiceException ex)
			{
				SetStatus(ProviderStatus.Error, $"{Strings.ConnectionErrorMessage} {ex.Message}");
			}
			catch (System.Net.Http.HttpRequestException ex)
			{
				SetStatus(ProviderStatus.Error, $"{Strings.ConnectionErrorMessage} {ex.Message}");
			}
		}
		finally
		{
			RequestSemaphore.Release();
		}
	}
}
