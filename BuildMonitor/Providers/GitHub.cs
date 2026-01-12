// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

using ktsu.Extensions;
using ktsu.Semantics.Strings;
using Octokit;

internal sealed class GitHub : BuildProvider
{
	internal static BuildProviderName BuildProviderName => nameof(GitHub).As<BuildProviderName>();
	internal override BuildProviderName Name => BuildProviderName;
	private GitHubClient GitHubClient { get; } = new(new ProductHeaderValue(Strings.FullyQualifiedApplicationName));
	private IRepositoriesClient GitHubRepository => GitHubClient.Repository;
	private IActionsClient GitHubActions => GitHubClient.Actions;
	private IActionsWorkflowsClient GitHubWorkflows => GitHubActions.Workflows;
	private IActionsWorkflowRunsClient GitHubRuns => GitHubWorkflows.Runs;
	private void UpdateGitHubClientCredentials()
	{
		if (!string.IsNullOrEmpty(AccountId) && !string.IsNullOrEmpty(Token))
		{
			GitHubClient.Credentials = new(AccountId, Token);
		}
	}

	internal override async Task UpdateRepositoriesAsync(Owner owner)
	{
		if (AccountId.IsEmpty() || Token.IsEmpty())
		{
			return;
		}

		UpdateGitHubClientCredentials();
		await MakeGitHubRequestAsync($"{Name}/{owner.Name}", async () =>
		{
			IReadOnlyList<Octokit.Repository> userRepositories = await GitHubRepository.GetAllForUser(owner.Name).ConfigureAwait(false);
			IReadOnlyList<Octokit.Repository> organizationRepositories = await GitHubRepository.GetAllForOrg(owner.Name).ConfigureAwait(false);
			IEnumerable<Octokit.Repository> allRepositories = userRepositories.Concat(organizationRepositories);

			foreach (Octokit.Repository? gitHubRepository in allRepositories)
			{
				RepositoryId repositoryId = gitHubRepository.Id.ToString(CultureInfo.InvariantCulture).As<RepositoryId>();

				if (gitHubRepository.Archived)
				{
					if (owner.Repositories.TryRemove(repositoryId, out _))
					{
						BuildMonitor.QueueSaveAppData();
					}
					continue;
				}

				RepositoryName repositoryName = gitHubRepository.Name.As<RepositoryName>();
				Repository repository = owner.CreateRepository(repositoryName, repositoryId);
				if (owner.Repositories.TryAdd(repositoryId, repository))
				{
					BuildMonitor.QueueSaveAppData();
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

		UpdateGitHubClientCredentials();
		try
		{
			await MakeGitHubRequestAsync($"{Name}/{repository.Owner.Name}/{repository.Name}", async () =>
			{
				WorkflowsResponse workflows = await GitHubWorkflows.List(repository.Owner.Name, repository.Name).ConfigureAwait(false);
				foreach (Workflow? workflow in workflows.Workflows)
				{
					BuildName buildName = workflow.Name.As<BuildName>();
					BuildId buildId = workflow.Id.ToString(CultureInfo.InvariantCulture).As<BuildId>();
					Build build = repository.CreateBuild(buildName, buildId);
					if (repository.Builds.TryAdd(buildId, build))
					{
						BuildMonitor.QueueSaveAppData();
					}
				}
			}).ConfigureAwait(false);
		}
		catch (NotFoundException)
		{
			// Repository not found
			repository.Owner.Repositories.TryRemove(repository.Id, out _);
		}
	}

	internal override async Task UpdateBuildAsync(Build build)
	{
		if (AccountId.IsEmpty() || Token.IsEmpty())
		{
			return;
		}

		UpdateGitHubClientCredentials();
		try
		{
			await MakeGitHubRequestAsync($"{Name}/{build.Owner.Name}/{build.Repository.Name}/{build.Name}", async () =>
		{
			WorkflowRunsResponse gitHubRuns = await GitHubRuns.ListByWorkflow(build.Owner.Name, build.Repository.Name, long.Parse(build.Id, CultureInfo.InvariantCulture), new(), new()
			{
				PageCount = 1,
				StartPage = 1,
				PageSize = 10,
			}).ConfigureAwait(false);
			foreach (WorkflowRun? gitHubRun in gitHubRuns.WorkflowRuns)
			{
				RunId runId = gitHubRun.Id.ToString(CultureInfo.InvariantCulture).As<RunId>();
				RunName runName = gitHubRun.Name.As<RunName>();
				Run run = build.Runs.GetOrCreate(runId, build.CreateRun(runName, runId));
				UpdateRunFromWorkflow(run, gitHubRun);
			}
		}).ConfigureAwait(false);
		}
		catch (NotFoundException)
		{
			// Build not found
			build.Repository.Builds.TryRemove(build.Id, out _);
		}
	}

	internal override async Task UpdateRunAsync(Run run)
	{
		if (AccountId.IsEmpty() || Token.IsEmpty())
		{
			return;
		}

		UpdateGitHubClientCredentials();

		try
		{
			await MakeGitHubRequestAsync($"{Name}/{run.Owner.Name}/{run.Repository.Name}/{run.Build.Name}/{run.Name}", async () =>
			{
				WorkflowRun gitHubRun = await GitHubRuns.Get(run.Owner.Name, run.Repository.Name, long.Parse(run.Id, CultureInfo.InvariantCulture)).ConfigureAwait(false);
				UpdateRunFromWorkflow(run, gitHubRun);
			}).ConfigureAwait(false);
		}
		catch (NotFoundException)
		{
			// Run not found
			run.Build.Runs.TryRemove(run.Id, out _);
		}
	}

	private static void UpdateRunFromWorkflow(Run run, WorkflowRun gitHubRun)
	{
		run.Started = gitHubRun.RunStartedAt;
		run.LastUpdated = gitHubRun.UpdatedAt;
		run.Status = gitHubRun.Conclusion switch
		{
			_ when gitHubRun.Status == WorkflowRunStatus.Requested => RunStatus.Pending,
			_ when gitHubRun.Status == WorkflowRunStatus.Queued => RunStatus.Pending,
			_ when gitHubRun.Status == WorkflowRunStatus.InProgress => RunStatus.Running,
			_ when gitHubRun.Status == WorkflowRunStatus.Completed && gitHubRun.Conclusion == WorkflowRunConclusion.Neutral => RunStatus.Success,
			_ when gitHubRun.Status == WorkflowRunStatus.Completed && gitHubRun.Conclusion == WorkflowRunConclusion.Success => RunStatus.Success,
			_ when gitHubRun.Status == WorkflowRunStatus.Completed && gitHubRun.Conclusion == WorkflowRunConclusion.Failure => RunStatus.Failure,
			_ when gitHubRun.Status == WorkflowRunStatus.Completed && gitHubRun.Conclusion == WorkflowRunConclusion.Cancelled => RunStatus.Canceled,
			_ when gitHubRun.Status == WorkflowRunStatus.Completed && gitHubRun.Conclusion == WorkflowRunConclusion.Skipped => RunStatus.Canceled,
			_ when gitHubRun.Status == WorkflowRunStatus.Completed && gitHubRun.Conclusion == WorkflowRunConclusion.StartupFailure => RunStatus.Failure,
			_ when gitHubRun.Status == WorkflowRunStatus.Completed && gitHubRun.Conclusion == WorkflowRunConclusion.TimedOut => RunStatus.Failure,
			_ when gitHubRun.Status == WorkflowRunStatus.Completed && gitHubRun.Conclusion == WorkflowRunConclusion.ActionRequired => RunStatus.Failure,
			_ when gitHubRun.Status == WorkflowRunStatus.Completed && gitHubRun.Conclusion == WorkflowRunConclusion.Stale => RunStatus.Failure,
			_ => throw new InvalidOperationException(),
		};

		run.Build.UpdateFromRun(run);
		BuildMonitor.QueueSaveAppData();
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0010:Add missing cases", Justification = "<Pending>")]
	internal async Task MakeGitHubRequestAsync(string name, Func<Task> action)
	{
		await Task.Delay((int)RateLimitSleep.TotalMilliseconds).ConfigureAwait(false);

		try
		{
			await BuildMonitor.MakeRequestAsync(name, action).ConfigureAwait(false);
		}
		catch (AuthorizationException)
		{
			OnAuthenticationFailure();
		}
		catch (ApiException e)
		{
			switch (e.HttpResponse?.StatusCode)
			{
				case System.Net.HttpStatusCode.Forbidden:
					OnAuthenticationFailure();
					break;
				case System.Net.HttpStatusCode.TooManyRequests:
					OnRateLimitExceeded();
					break;
				default:
					throw;
			}
		}
	}
}
