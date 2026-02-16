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
	private bool ShouldDiscoverProjects { get; set; }

	private void UpdateAzureDevOpsClientCredentials()
	{
		if (!string.IsNullOrEmpty(AccountId) && !string.IsNullOrEmpty(Token))
		{
			try
			{
				Uri collectionUri = new($"https://dev.azure.com/{AccountId}");
				Log.Debug($"{Name}: Connecting to {collectionUri}");
				VssBasicCredential credentials = new(string.Empty, Token);
				Connection = new(collectionUri, credentials);
				ProjectClient = Connection.GetClient<ProjectHttpClient>();
				BuildClient = Connection.GetClient<BuildHttpClient>();
				Log.Debug($"{Name}: ADO clients initialized successfully");
			}
			catch (VssServiceException ex)
			{
				Log.Error($"{Name}: Failed to initialize ADO clients - VssServiceException: {ex.Message}");
				Connection = null;
				ProjectClient = null;
				BuildClient = null;
				SetStatus(ProviderStatus.Error, $"{Strings.ConnectionErrorMessage} {ex.Message}");
			}
			catch (UriFormatException ex)
			{
				Log.Error($"{Name}: Failed to initialize ADO clients - Invalid URI: {ex.Message}");
				Connection = null;
				ProjectClient = null;
				BuildClient = null;
				SetStatus(ProviderStatus.Error, $"Invalid organization name: {ex.Message}");
			}
		}
		else
		{
			Log.Debug($"{Name}: UpdateAzureDevOpsClientCredentials skipped - AccountId empty: {string.IsNullOrEmpty(AccountId)}, Token empty: {string.IsNullOrEmpty(Token)}");
		}
	}

	internal override void ShowMenu()
	{
		if (Hexa.NET.ImGui.ImGui.BeginMenu(Name))
		{
			if (Hexa.NET.ImGui.ImGui.MenuItem(Strings.SetCredentials))
			{
				// Trigger credentials popup via base class mechanism
				TriggerCredentialsPopup();
			}

			if (Hexa.NET.ImGui.ImGui.MenuItem(Strings.DiscoverAllProjects))
			{
				ShouldDiscoverProjects = true;
			}

			if (Hexa.NET.ImGui.ImGui.MenuItem(Strings.AddOwner))
			{
				TriggerAddOwnerPopup();
			}

			Hexa.NET.ImGui.ImGui.EndMenu();
		}
	}

	internal override void Tick()
	{
		base.Tick();

		if (ShouldDiscoverProjects)
		{
			ShouldDiscoverProjects = false;
			_ = DiscoverProjectsAsync();
		}
	}

	internal async Task DiscoverProjectsAsync()
	{
		if (AccountId.IsEmpty() || Token.IsEmpty())
		{
			Log.Warning($"{Name}: DiscoverProjectsAsync skipped - AccountId or Token is empty");
			return;
		}

		UpdateAzureDevOpsClientCredentials();
		if (ProjectClient == null)
		{
			Log.Warning($"{Name}: DiscoverProjectsAsync skipped - ProjectClient is null after credential update");
			return;
		}

		Log.Info($"{Name}: Discovering projects for organization '{AccountId}'");
		await MakeAzureDevOpsRequestAsync($"{Name}/discover", async () =>
		{
			IEnumerable<TeamProjectReference> projects = await ProjectClient.GetProjects().ConfigureAwait(false);

			int projectCount = 0;
			foreach (TeamProjectReference? project in projects)
			{
				projectCount++;
				OwnerName ownerName = project.Name.As<OwnerName>();
				if (Owners.TryAdd(ownerName, CreateOwner(ownerName)))
				{
					Log.Info($"{Name}: Discovered new project '{project.Name}' (ID: {project.Id})");
					BuildMonitor.QueueSaveAppData();
				}

				// Always ensure repository entry exists for this project
				Owner owner = Owners[ownerName];
				RepositoryId repositoryId = project.Id.ToString().As<RepositoryId>();
				RepositoryName repositoryName = project.Name.As<RepositoryName>();
				_ = owner.Repositories.GetOrAdd(repositoryId, _ => owner.CreateRepository(repositoryName, repositoryId));
			}
			Log.Info($"{Name}: DiscoverProjects found {projectCount} project(s)");
		}).ConfigureAwait(false);
	}

	internal override async Task UpdateRepositoriesAsync(Owner owner)
	{
		if (AccountId.IsEmpty() || Token.IsEmpty())
		{
			Log.Warning($"{Name}: UpdateRepositoriesAsync skipped for owner '{owner.Name}' - AccountId or Token is empty");
			return;
		}

		UpdateAzureDevOpsClientCredentials();
		if (ProjectClient == null)
		{
			Log.Warning($"{Name}: UpdateRepositoriesAsync skipped for owner '{owner.Name}' - ProjectClient is null");
			return;
		}

		Log.Debug($"{Name}: UpdateRepositoriesAsync for owner '{owner.Name}'");
		await MakeAzureDevOpsRequestAsync($"{Name}/{owner.Name}", async () =>
		{
			// In Azure DevOps, projects are the top-level containers
			// The owner name represents a project in Azure DevOps
			IEnumerable<TeamProjectReference> projects = await ProjectClient.GetProjects().ConfigureAwait(false);

			bool foundProject = false;
			int projectCount = 0;
			foreach (TeamProjectReference? project in projects)
			{
				projectCount++;
				// Use case-insensitive comparison for project names
				if (string.Equals(project.Name, owner.Name, StringComparison.OrdinalIgnoreCase))
				{
					foundProject = true;
					RepositoryId repositoryId = project.Id.ToString().As<RepositoryId>();
					RepositoryName repositoryName = project.Name.As<RepositoryName>();

					// Get existing repository or create new one
					bool isNew = !owner.Repositories.ContainsKey(repositoryId);
					_ = owner.Repositories.GetOrAdd(repositoryId, _ => owner.CreateRepository(repositoryName, repositoryId));

					if (isNew)
					{
						Log.Info($"{Name}: Added repository '{repositoryName}' (ID: {repositoryId}) for owner '{owner.Name}'");
						BuildMonitor.QueueSaveAppData();
					}
					break;
				}
			}

			Log.Debug($"{Name}: UpdateRepositories listed {projectCount} project(s), match for '{owner.Name}': {foundProject}");

			if (!foundProject)
			{
				Log.Warning($"{Name}: Project '{owner.Name}' not found among {projectCount} projects in organization '{AccountId}'");
				SetStatus(ProviderStatus.Error, $"Project '{owner.Name}' not found in organization '{AccountId}'");
			}
		}).ConfigureAwait(false);
	}

	internal override async Task UpdateBuildsAsync(Repository repository)
	{
		if (AccountId.IsEmpty() || Token.IsEmpty())
		{
			Log.Warning($"{Name}: UpdateBuildsAsync skipped for '{repository.Owner.Name}/{repository.Name}' - AccountId or Token is empty");
			return;
		}

		UpdateAzureDevOpsClientCredentials();
		if (BuildClient == null)
		{
			Log.Warning($"{Name}: UpdateBuildsAsync skipped for '{repository.Owner.Name}/{repository.Name}' - BuildClient is null");
			return;
		}

		Log.Debug($"{Name}: UpdateBuildsAsync for '{repository.Owner.Name}/{repository.Name}'");
		await MakeAzureDevOpsRequestAsync($"{Name}/{repository.Owner.Name}/{repository.Name}", async () =>
		{
			List<BuildDefinitionReference> definitions = await BuildClient.GetDefinitionsAsync(repository.Owner.Name).ConfigureAwait(false);
			Log.Info($"{Name}: Found {definitions.Count} build definition(s) for '{repository.Owner.Name}/{repository.Name}'");
			foreach (BuildDefinitionReference? definition in definitions)
			{
				BuildName buildName = definition.Name.As<BuildName>();
				BuildId buildId = definition.Id.ToString(CultureInfo.InvariantCulture).As<BuildId>();

				// Get existing build or create new one
				bool isNew = false;
				Build build = repository.Builds.GetOrAdd(buildId, _ =>
				{
					isNew = true;
					return repository.CreateBuild(buildName, buildId);
				});

				// Update name if it changed (e.g., build definition renamed)
				bool hasChanges = false;
				if (build.Name != buildName)
				{
					build.Name = buildName;
					hasChanges = true;
				}

				if (isNew)
				{
					Log.Info($"{Name}: Added new build definition '{buildName}' (ID: {buildId}) for '{repository.Owner.Name}/{repository.Name}'");
				}

				if (isNew || hasChanges)
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
			Log.Warning($"{Name}: UpdateBuildAsync skipped for '{build.Owner.Name}/{build.Repository.Name}/{build.Name}' - AccountId or Token is empty");
			return;
		}

		UpdateAzureDevOpsClientCredentials();
		if (BuildClient == null)
		{
			Log.Warning($"{Name}: UpdateBuildAsync skipped for '{build.Owner.Name}/{build.Repository.Name}/{build.Name}' - BuildClient is null");
			return;
		}

		Log.Debug($"{Name}: UpdateBuildAsync for '{build.Owner.Name}/{build.Repository.Name}/{build.Name}' (definition ID: {build.Id})");
		await MakeAzureDevOpsRequestAsync($"{Name}/{build.Owner.Name}/{build.Repository.Name}/{build.Name}", async () =>
		{
			List<Microsoft.TeamFoundation.Build.WebApi.Build> builds = await BuildClient.GetBuildsAsync(
				build.Owner.Name,
				definitions: [int.Parse(build.Id, CultureInfo.InvariantCulture)],
				top: 10
			).ConfigureAwait(false);

			Log.Info($"{Name}: GetBuildsAsync returned {builds.Count} run(s) for '{build.Owner.Name}/{build.Repository.Name}/{build.Name}'");
			foreach (Microsoft.TeamFoundation.Build.WebApi.Build? azureBuild in builds)
			{
				RunId runId = azureBuild.Id.ToString(CultureInfo.InvariantCulture).As<RunId>();
				RunName runName = $"{azureBuild.BuildNumber}".As<RunName>();
				Log.Debug($"{Name}: Processing run '{runName}' (ID: {runId}, Status: {azureBuild.Status}, Result: {azureBuild.Result}, Branch: {azureBuild.SourceBranch})");
				Run run = build.Runs.GetOrCreate(runId, build.CreateRun(runName, runId));
				UpdateRunFromBuild(run, azureBuild);
			}
		}).ConfigureAwait(false);
	}

	internal override async Task UpdateRunAsync(Run run)
	{
		if (AccountId.IsEmpty() || Token.IsEmpty())
		{
			Log.Warning($"{Name}: UpdateRunAsync skipped for run '{run.Name}' - AccountId or Token is empty");
			return;
		}

		UpdateAzureDevOpsClientCredentials();
		if (BuildClient == null)
		{
			Log.Warning($"{Name}: UpdateRunAsync skipped for run '{run.Name}' - BuildClient is null");
			return;
		}

		Log.Debug($"{Name}: UpdateRunAsync for run '{run.Name}' (ID: {run.Id}) in '{run.Owner.Name}/{run.Repository.Name}/{run.Build.Name}'");
		await MakeAzureDevOpsRequestAsync($"{Name}/{run.Owner.Name}/{run.Repository.Name}/{run.Build.Name}/{run.Name}", async () =>
		{
			Microsoft.TeamFoundation.Build.WebApi.Build azureBuild = await BuildClient.GetBuildAsync(
				run.Owner.Name,
				int.Parse(run.Id, CultureInfo.InvariantCulture)
			).ConfigureAwait(false);
			Log.Debug($"{Name}: Run '{run.Name}' updated - Status: {azureBuild.Status}, Result: {azureBuild.Result}, Branch: {azureBuild.SourceBranch}");
			UpdateRunFromBuild(run, azureBuild);
		}).ConfigureAwait(false);
	}

	private static void UpdateRunFromBuild(Run run, Microsoft.TeamFoundation.Build.WebApi.Build build)
	{
		// Use QueueTime as fallback for StartTime if not yet started
		run.Started = build.StartTime ?? build.QueueTime ?? DateTimeOffset.UtcNow;

		// For LastUpdated, use current time if not finished yet (in-progress builds)
		run.LastUpdated = build.FinishTime ?? DateTimeOffset.UtcNow;

		string branch = build.SourceBranch ?? string.Empty;
		if (branch.StartsWith("refs/heads/", StringComparison.Ordinal))
		{
			branch = branch["refs/heads/".Length..];
		}
		run.Branch = branch.As<BranchName>();

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
			if (waitTime > BaseRequestDelay)
			{
				Log.Debug($"{Name}: Rate limit pacing - waiting {waitTime.TotalMilliseconds:F0}ms before request '{name}'");
			}
			await Task.Delay(waitTime).ConfigureAwait(false);

			try
			{
				await BuildMonitor.MakeRequestAsync(name, action).ConfigureAwait(false);
				ClearStatus();
			}
			catch (VssServiceResponseException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized)
			{
				Log.Error($"{Name}: 401 Unauthorized for request '{name}' - {ex.Message}");
				OnAuthenticationFailure();
			}
			catch (VssServiceResponseException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.TooManyRequests)
			{
				Log.Warning($"{Name}: 429 Too Many Requests for '{name}' - {ex.Message}");
				OnRateLimitExceeded();
			}
			catch (VssServiceException ex)
			{
				Log.Error($"{Name}: VssServiceException for request '{name}' - {ex.Message}");
				SetStatus(ProviderStatus.Error, $"{Strings.ConnectionErrorMessage} {ex.Message}");
			}
			catch (HttpRequestException ex)
			{
				Log.Error($"{Name}: HttpRequestException for request '{name}' - {ex.Message}");
				SetStatus(ProviderStatus.Error, $"{Strings.ConnectionErrorMessage} {ex.Message}");
			}
		}
		finally
		{
			RequestSemaphore.Release();
		}
	}
}
