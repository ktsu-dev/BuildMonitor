// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using ktsu.Extensions;
using ktsu.Semantics.Strings;
using Octokit;

internal sealed partial class GitHub : BuildProvider
{
	internal static BuildProviderName BuildProviderName => nameof(GitHub).As<BuildProviderName>();
	internal override BuildProviderName Name => BuildProviderName;
	private GitHubClient GitHubClient { get; } = new(new ProductHeaderValue(Strings.FullyQualifiedApplicationName));
	private IRepositoriesClient GitHubRepository => GitHubClient.Repository;
	private IActionsClient GitHubActions => GitHubClient.Actions;
	private IActionsWorkflowsClient GitHubWorkflows => GitHubActions.Workflows;
	private IActionsWorkflowRunsClient GitHubRuns => GitHubWorkflows.Runs;
	private IActionsWorkflowJobsClient GitHubJobs => GitHubWorkflows.Jobs;
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
				await UpdateRunFromWorkflowAsync(run, gitHubRun).ConfigureAwait(false);
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
				await UpdateRunFromWorkflowAsync(run, gitHubRun).ConfigureAwait(false);
			}).ConfigureAwait(false);
		}
		catch (NotFoundException)
		{
			// Run not found
			run.Build.Runs.TryRemove(run.Id, out _);
		}
	}

	private async Task UpdateRunFromWorkflowAsync(Run run, WorkflowRun gitHubRun)
	{
		run.Started = gitHubRun.RunStartedAt;
		run.LastUpdated = gitHubRun.UpdatedAt;
		run.Branch = gitHubRun.HeadBranch.As<BranchName>();

		RunStatus previousStatus = run.Status;
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

		// Fetch error details if the run just failed or is a failure without errors
		if (run.Status == RunStatus.Failure && (previousStatus != RunStatus.Failure || run.Errors.Count == 0))
		{
			await FetchRunErrorsAsync(run).ConfigureAwait(false);
		}

		// Clear errors if the run is no longer a failure
		if (run.Status != RunStatus.Failure && run.Errors.Count > 0)
		{
			run.Errors.Clear();
		}

		run.Build.UpdateFromRun(run);
		BuildMonitor.QueueSaveAppData();
	}

	private async Task FetchRunErrorsAsync(Run run)
	{
		try
		{
			WorkflowJobsResponse jobs = await GitHubJobs.List(run.Owner.Name, run.Repository.Name, long.Parse(run.Id, CultureInfo.InvariantCulture)).ConfigureAwait(false);

			List<string> errors = [];
			foreach (WorkflowJob? job in jobs.Jobs)
			{
				if (job.Conclusion == WorkflowJobConclusion.Failure)
				{
					// Try to fetch and parse the job logs for actual error messages
					List<string> logErrors = await FetchJobLogErrorsAsync(run.Owner.Name, run.Repository.Name, job.Id).ConfigureAwait(false);

					if (logErrors.Count > 0)
					{
						foreach (string logError in logErrors)
						{
							errors.Add($"[{job.Name}] {logError}");
						}
					}
					else
					{
						// Fall back to failed step names if no log errors found
						List<WorkflowJobStep> failedSteps = [.. job.Steps.Where(s => s.Conclusion == WorkflowJobConclusion.Failure)];
						if (failedSteps.Count > 0)
						{
							foreach (WorkflowJobStep step in failedSteps)
							{
								errors.Add($"[{job.Name}] {step.Name}");
							}
						}
						else
						{
							errors.Add($"[{job.Name}] Failed");
						}
					}
				}
			}

			run.Errors = errors;
		}
		catch (NotFoundException)
		{
			// Jobs not found, ignore
		}
		catch (ApiException)
		{
			// API error, ignore
		}
	}

	private async Task<List<string>> FetchJobLogErrorsAsync(string owner, string repo, long jobId)
	{
		List<string> errors = [];
		try
		{
			string logs = await GitHubJobs.GetLogs(owner, repo, jobId).ConfigureAwait(false);
			errors = ParseLogForErrors(logs);
		}
		catch (NotFoundException)
		{
			// Logs not found, ignore
		}
		catch (ApiException)
		{
			// API error, ignore
		}
		return errors;
	}

	[GeneratedRegex(@"(?:^|\s)error\s*:", RegexOptions.IgnoreCase)]
	private static partial Regex ErrorPatternRegex();

	private static List<string> ParseLogForErrors(string logs)
	{
		List<string> errors = [];
		HashSet<string> seenErrors = new(StringComparer.OrdinalIgnoreCase);

		string[] lines = logs.Split('\n');
		foreach (string line in lines)
		{
			string trimmedLine = line.Trim();

			// GitHub Actions error annotations: ##[error]message
			if (trimmedLine.Contains("##[error]", StringComparison.OrdinalIgnoreCase))
			{
				int errorIndex = trimmedLine.IndexOf("##[error]", StringComparison.OrdinalIgnoreCase);
				string errorMessage = trimmedLine[(errorIndex + 9)..].Trim();
				if (!string.IsNullOrWhiteSpace(errorMessage) && seenErrors.Add(errorMessage))
				{
					errors.Add(errorMessage);
				}
			}
			// Common error patterns: "error:" or "Error:" at start or after timestamp
			else if (ErrorPatternRegex().IsMatch(trimmedLine))
			{
				int errorIndex = trimmedLine.IndexOf("error", StringComparison.OrdinalIgnoreCase);
				int colonIndex = trimmedLine.IndexOf(':', errorIndex);
				if (colonIndex >= 0 && colonIndex < trimmedLine.Length - 1)
				{
					string errorMessage = trimmedLine[(colonIndex + 1)..].Trim();
					if (!string.IsNullOrWhiteSpace(errorMessage) && seenErrors.Add(errorMessage))
					{
						errors.Add(errorMessage);
					}
				}
			}
		}

		// Limit to first 10 errors to avoid overwhelming the UI
		return [.. errors.Take(10)];
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
