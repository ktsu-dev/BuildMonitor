// Ignore Spelling: workflow workflows

namespace ktsu.BuildMonitor;

using System.Globalization;
using System.Threading.Tasks;
using ktsu.Extensions;
using Octokit;

internal class GitHub : BuildProvider
{
	internal static BuildProviderName BuildProviderName => (BuildProviderName)nameof(GitHub);
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
		UpdateGitHubClientCredentials();
		await MakeGitHubRequestAsync($"{Name}/{owner.Name}", async () =>
		{
			var userRepositories = await GitHubRepository.GetAllForUser(owner.Name);
			var organizationRepositories = await GitHubRepository.GetAllForOrg(owner.Name);
			var allRepositories = userRepositories.Concat(organizationRepositories);

			foreach (var gitHubRepository in allRepositories)
			{
				var repositoryName = (RepositoryName)gitHubRepository.Name;
				var repositoryId = (RepositoryId)gitHubRepository.Id.ToString(CultureInfo.InvariantCulture);
				var repository = owner.CreateRepository(repositoryName, repositoryId);
				if (owner.Repositories.TryAdd(repositoryId, repository))
				{
					BuildMonitor.QueueSaveAppData();
				}
			}
		});
	}

	internal override async Task UpdateBuildsAsync(Repository repository)
	{
		UpdateGitHubClientCredentials();
		await MakeGitHubRequestAsync($"{Name}/{repository.Owner.Name}/{repository.Name}", async () =>
		{
			var workflows = await GitHubWorkflows.List(repository.Owner.Name, repository.Name);
			foreach (var workflow in workflows.Workflows)
			{
				var buildName = (BuildName)workflow.Name;
				var buildId = (BuildId)workflow.Id.ToString(CultureInfo.InvariantCulture);
				var build = repository.CreateBuild(buildName, buildId);
				if (repository.Builds.TryAdd(buildId, build))
				{
					BuildMonitor.QueueSaveAppData();
				}
			}
		});
	}

	internal override async Task UpdateBuildAsync(Build build)
	{
		UpdateGitHubClientCredentials();
		await MakeGitHubRequestAsync($"{Name}/{build.Owner.Name}/{build.Repository.Name}/{build.Name}", async () =>
		{
			var gitHubRuns = await GitHubRuns.ListByWorkflow(build.Owner.Name, build.Repository.Name, long.Parse(build.Id, CultureInfo.InvariantCulture), new(), new()
			{
				PageCount = 1,
				StartPage = 1,
				PageSize = 10,
			});
			foreach (var gitHubRun in gitHubRuns.WorkflowRuns)
			{
				var runId = (RunId)gitHubRun.Id.ToString(CultureInfo.InvariantCulture);
				var runName = (RunName)gitHubRun.Name;
				var run = build.Runs.GetOrCreate(runId, build.CreateRun(runName, runId));
				UpdateRunFromWorkflow(build, run, gitHubRun);
			}
		});
	}

	internal override async Task UpdateRunAsync(Build build, Run run)
	{
		UpdateGitHubClientCredentials();
		await MakeGitHubRequestAsync($"{Name}/{run.Owner.Name}/{run.Repository.Name}/{run.Build.Name}/{run.Name}", async () =>
		{
			var gitHubRun = await GitHubRuns.Get(run.Owner.Name, run.Repository.Name, long.Parse(run.Id, CultureInfo.InvariantCulture));
			UpdateRunFromWorkflow(build, run, gitHubRun);
		});
	}

	private void UpdateRunFromWorkflow(Build build, Run run, WorkflowRun gitHubRun)
	{
		run.Started = gitHubRun.RunStartedAt;
		run.LastUpdated = gitHubRun.UpdatedAt;

		if (gitHubRun.Conclusion == WorkflowRunConclusion.Failure)
		{
			string log = System.Text.Encoding.Default.GetString(GitHubRuns.GetAttemptLogs(build.Owner.Name, build.Name, gitHubRun.Id, gitHubRun.RunAttempt).Result);

			if (log.Contains("Cancelled the workflow run"))
			{
				run.Status = RunStatus.Canceled;
			}
		}
		else
		{
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
		}

		run.Build.UpdateFromRun(run);
		BuildMonitor.QueueSaveAppData();
	}

	internal static async Task MakeGitHubRequestAsync(string name, Func<Task> action)
	{
		try
		{
			await BuildMonitor.MakeRequestAsync(name, action);
		}
		catch (ApiException e)
		{
			if (e.HttpResponse.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
			{
				throw;
			}
		}
	}
}
