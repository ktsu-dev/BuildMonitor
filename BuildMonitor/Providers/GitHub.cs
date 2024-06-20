// Ignore Spelling: workflow workflows

namespace ktsu.io.BuildMonitor;

using System.Globalization;
using System.Threading.Tasks;
using ktsu.io.Extensions;
using Octokit;

internal class GitHub : BuildProvider
{
	internal static BuildProviderName BuildProviderName => (BuildProviderName)nameof(GitHub);
	internal override BuildProviderName Name => BuildProviderName;
	private GitHubClient GitHubClient { get; } = new(new ProductHeaderValue(Strings.FullyQualifiedApplicationName));

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
		BuildMonitor.LastRequest = $"GitHubClient.Repository.GetAllForUser({owner.Name})";
		var userRepositories = await GitHubClient.Repository.GetAllForUser(owner.Name);
		BuildMonitor.LastRequest += " (done)";
		BuildMonitor.LastRequest = $"GitHubClient.Repository.GetAllForOrg({owner.Name})";
		var organizationRepositories = await GitHubClient.Repository.GetAllForOrg(owner.Name);
		BuildMonitor.LastRequest += " (done)";
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
	}

	internal override async Task UpdateBuildsAsync(Repository repository)
	{
		UpdateGitHubClientCredentials();
		BuildMonitor.LastRequest = $"GitHubClient.Actions.Workflows.List({repository.Owner.Name}, {repository.Name})";
		var workflows = await GitHubClient.Actions.Workflows.List(repository.Owner.Name, repository.Name);
		BuildMonitor.LastRequest += " (done)";
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
	}

	internal override async Task UpdateBuildAsync(Build build)
	{
		UpdateGitHubClientCredentials();
		BuildMonitor.LastRequest = $"GitHubClient.Actions.Workflows.Runs.ListByWorkflow({build.Owner.Name}, {build.Repository.Name}, {build.Name})";
		var gitHubRuns = await GitHubClient.Actions.Workflows.Runs.ListByWorkflow(build.Owner.Name, build.Repository.Name, long.Parse(build.Id, CultureInfo.InvariantCulture), new(), new()
		{
			PageCount = 1,
			StartPage = 1,
			PageSize = 10,
		});
		BuildMonitor.LastRequest += " (done)";
		foreach (var gitHubRun in gitHubRuns.WorkflowRuns)
		{
			var runId = (RunId)gitHubRun.Id.ToString(CultureInfo.InvariantCulture);
			var runName = (RunName)gitHubRun.Name;
			var run = build.Runs.GetOrCreate(runId, build.CreateRun(runName, runId));
			UpdateRunFromWorkflow(run, gitHubRun);
		}
	}

	internal override async Task UpdateRunAsync(Run run)
	{
		UpdateGitHubClientCredentials();
		BuildMonitor.LastRequest = $"GitHubClient.Actions.Workflows.Runs.Get({run.Owner.Name}, {run.Repository.Name}, {run.Name})";
		var gitHubRun = await GitHubClient.Actions.Workflows.Runs.Get(run.Owner.Name, run.Repository.Name, long.Parse(run.Id, CultureInfo.InvariantCulture));
		BuildMonitor.LastRequest += " (done)";
		UpdateRunFromWorkflow(run, gitHubRun);
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
}
