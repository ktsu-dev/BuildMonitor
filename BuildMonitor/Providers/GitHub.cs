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

[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling", Justification = "Provider class requires many type dependencies for GitHub API integration")]
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
	private bool ShouldDiscoverOwners { get; set; }

	/// <summary>
	/// Updates GitHub client credentials, using owner-specific token if available.
	/// </summary>
	/// <param name="owner">Optional owner to check for specific token.</param>
	private void UpdateGitHubClientCredentials(Owner? owner = null)
	{
		// Use owner-specific token if available, otherwise fall back to provider token
		string tokenToUse = owner?.HasToken == true ? owner.Token : Token;

		if (!string.IsNullOrEmpty(AccountId) && !string.IsNullOrEmpty(tokenToUse))
		{
			GitHubClient.Credentials = new(AccountId, tokenToUse);
		}
	}

	/// <summary>
	/// Checks if we have valid credentials for the specified owner.
	/// </summary>
	/// <param name="owner">Optional owner to check for specific token.</param>
	/// <returns>True if valid credentials are available.</returns>
	private bool HasValidCredentials(Owner? owner = null)
	{
		string tokenToUse = owner?.HasToken == true ? owner.Token : Token;
		return !AccountId.IsEmpty() && !string.IsNullOrEmpty(tokenToUse);
	}

	private Owner? OwnerPendingTokenPopup { get; set; }

	internal override void ShowMenu()
	{
		if (Hexa.NET.ImGui.ImGui.BeginMenu(Name))
		{
			if (Hexa.NET.ImGui.ImGui.MenuItem(Strings.SetCredentials))
			{
				TriggerCredentialsPopup();
			}

			if (Hexa.NET.ImGui.ImGui.MenuItem(Strings.DiscoverAllOwners))
			{
				ShouldDiscoverOwners = true;
			}

			if (Hexa.NET.ImGui.ImGui.MenuItem(Strings.AddOwner))
			{
				TriggerAddOwnerPopup();
			}

			// Owner token submenu
			if (!Owners.IsEmpty && Hexa.NET.ImGui.ImGui.BeginMenu(Strings.SetOwnerToken))
			{
				foreach ((OwnerName ownerName, Owner owner) in Owners.OrderBy(o => o.Key.ToString()))
				{
					string menuLabel = owner.HasToken
						? $"{ownerName} {Strings.OwnerHasToken}"
						: ownerName.ToString();

					if (Hexa.NET.ImGui.ImGui.MenuItem(menuLabel))
					{
						OwnerPendingTokenPopup = owner;
					}
				}

				Hexa.NET.ImGui.ImGui.Separator();
				if (Hexa.NET.ImGui.ImGui.BeginMenu(Strings.ClearOwnerToken))
				{
					foreach ((OwnerName ownerName, Owner owner) in Owners.Where(o => o.Value.HasToken).OrderBy(o => o.Key.ToString()))
					{
						if (Hexa.NET.ImGui.ImGui.MenuItem(ownerName.ToString()))
						{
							owner.Token = new();
							BuildMonitor.QueueSaveAppData();
							Log.Info($"GitHub: Cleared token for owner {ownerName}");
						}
					}
					Hexa.NET.ImGui.ImGui.EndMenu();
				}

				Hexa.NET.ImGui.ImGui.EndMenu();
			}

			Hexa.NET.ImGui.ImGui.EndMenu();
		}
	}

	private ktsu.ImGui.Popups.ImGuiPopups.InputString OwnerTokenPopup { get; } = new();

	internal override void Tick()
	{
		base.Tick();

		if (ShouldDiscoverOwners)
		{
			ShouldDiscoverOwners = false;
			_ = DiscoverOwnersAsync();
		}

		// Handle owner token popup
		if (OwnerPendingTokenPopup is not null)
		{
			Owner owner = OwnerPendingTokenPopup;
			OwnerPendingTokenPopup = null;
			OwnerTokenPopup.Open($"{Strings.SetOwnerToken}: {owner.Name}", Strings.Token, string.Empty, (result) =>
			{
				owner.Token = result.As<BuildProviderToken>();
				BuildMonitor.QueueSaveAppData();
				Log.Info($"GitHub: Set token for owner {owner.Name}");
			});
		}

		_ = OwnerTokenPopup.ShowIfOpen();
	}

	internal async Task DiscoverOwnersAsync()
	{
		if (!HasValidCredentials())
		{
			return;
		}

		Log.Info($"GitHub: Discovering owners for account {AccountId}");
		UpdateGitHubClientCredentials();

		await MakeGitHubRequestAsync($"{Name}/discover", async () =>
		{
			// Add the authenticated user as an owner
			User currentUser = await GitHubClient.User.Current().ConfigureAwait(false);
			OwnerName userName = currentUser.Login.As<OwnerName>();
			if (Owners.TryAdd(userName, CreateOwner(userName)))
			{
				Log.Info($"GitHub: Discovered user owner: {userName}");
				BuildMonitor.QueueSaveAppData();
			}

			// Add all organizations the user belongs to
			IReadOnlyList<Organization> organizations = await GitHubClient.Organization.GetAllForCurrent().ConfigureAwait(false);
			foreach (Organization org in organizations)
			{
				OwnerName orgName = org.Login.As<OwnerName>();
				if (Owners.TryAdd(orgName, CreateOwner(orgName)))
				{
					Log.Info($"GitHub: Discovered organization owner: {orgName}");
					BuildMonitor.QueueSaveAppData();
				}
			}

			Log.Info($"GitHub: Owner discovery complete. Found {Owners.Count} owners");
		}).ConfigureAwait(false);
	}

	internal override async Task UpdateRepositoriesAsync(Owner owner)
	{
		if (!HasValidCredentials(owner))
		{
			return;
		}

		UpdateGitHubClientCredentials(owner);
		await MakeGitHubRequestAsync($"{Name}/{owner.Name}", async () =>
		{
			List<Octokit.Repository> allRepositories = [];

			// Check if this owner is the authenticated user - if so, use GetAllForCurrent to get private repos
			User? currentUser = null;
			try
			{
				currentUser = await GitHubClient.User.Current().ConfigureAwait(false);
			}
			catch (AuthorizationException)
			{
				// Not authenticated, can't get current user
			}

			if (currentUser != null && currentUser.Login.Equals(owner.Name.ToString(), StringComparison.OrdinalIgnoreCase))
			{
				// This is the authenticated user - get all repos including private ones
				IReadOnlyList<Octokit.Repository> currentUserRepos = await GitHubRepository.GetAllForCurrent().ConfigureAwait(false);
				allRepositories.AddRange(currentUserRepos);
				Log.Info($"GitHub: Found {currentUserRepos.Count} repositories for authenticated user {owner.Name} (including private)");
			}
			else
			{
				// Try to get repositories for user - this works for both users and orgs but only public repos
				IReadOnlyList<Octokit.Repository> userRepositories;
				try
				{
					userRepositories = await GitHubRepository.GetAllForUser(owner.Name).ConfigureAwait(false);
					allRepositories.AddRange(userRepositories);
				}
				catch (NotFoundException)
				{
					// Owner might be an org-only account, try org repos instead
					userRepositories = [];
				}

				// Try to get organization repositories - this only works for orgs
				IReadOnlyList<Octokit.Repository> organizationRepositories;
				try
				{
					organizationRepositories = await GitHubRepository.GetAllForOrg(owner.Name).ConfigureAwait(false);
					allRepositories.AddRange(organizationRepositories);
				}
				catch (NotFoundException)
				{
					// Owner is not an organization, that's fine
					organizationRepositories = [];
				}
			}

			int newRepos = 0;
			int archivedRepos = 0;
			foreach (Octokit.Repository? gitHubRepository in allRepositories)
			{
				RepositoryId repositoryId = gitHubRepository.Id.ToString(CultureInfo.InvariantCulture).As<RepositoryId>();

				if (gitHubRepository.Archived)
				{
					if (owner.Repositories.TryRemove(repositoryId, out _))
					{
						archivedRepos++;
						BuildMonitor.QueueSaveAppData();
					}
					continue;
				}

				RepositoryName repositoryName = gitHubRepository.Name.As<RepositoryName>();
				Repository repository = owner.CreateRepository(repositoryName, repositoryId);
				if (owner.Repositories.TryAdd(repositoryId, repository))
				{
					newRepos++;
					Log.Info($"GitHub: Discovered repository: {owner.Name}/{repositoryName}");
					BuildMonitor.QueueSaveAppData();
				}
			}

			if (newRepos > 0 || archivedRepos > 0)
			{
				Log.Info($"GitHub: Repository update for {owner.Name}: {newRepos} new, {archivedRepos} archived removed, {owner.Repositories.Count} total");
			}
		}).ConfigureAwait(false);
	}

	internal override async Task UpdateBuildsAsync(Repository repository)
	{
		if (!HasValidCredentials(repository.Owner))
		{
			return;
		}

		UpdateGitHubClientCredentials(repository.Owner);
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
		if (!HasValidCredentials(build.Owner))
		{
			return;
		}

		UpdateGitHubClientCredentials(build.Owner);
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
		if (!HasValidCredentials(run.Owner))
		{
			return;
		}

		UpdateGitHubClientCredentials(run.Owner);

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

		// Log status transitions
		if (previousStatus != run.Status)
		{
			if (run.Status == RunStatus.Failure)
			{
				Log.Warning($"Build failed: {run.Owner.Name}/{run.Repository.Name}/{run.Build.Name} on {run.Branch}");
			}
			else if (run.Status == RunStatus.Success && previousStatus == RunStatus.Running)
			{
				Log.Info($"Build succeeded: {run.Owner.Name}/{run.Repository.Name}/{run.Build.Name} on {run.Branch}");
			}
			else if (run.Status == RunStatus.Running && previousStatus != RunStatus.Running)
			{
				Log.Info($"Build started: {run.Owner.Name}/{run.Repository.Name}/{run.Build.Name} on {run.Branch}");
			}
		}

		// Fetch error details if the run just failed or is a failure without errors
		if (run.Status == RunStatus.Failure && (previousStatus != RunStatus.Failure || run.Errors.Count == 0))
		{
			await FetchRunErrorsAsync(run).ConfigureAwait(false);
		}

		// Clear errors if the run is no longer a failure
		if (run.Status != RunStatus.Failure && run.Errors.Count > 0)
		{
			run.Errors = [];
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
		await RequestSemaphore.WaitAsync().ConfigureAwait(false);
		try
		{
			// Use smart waiting: if rate limited with a known reset time, wait until reset
			TimeSpan waitTime = GetRateLimitWaitTime();
			await Task.Delay(waitTime).ConfigureAwait(false);

			try
			{
				await BuildMonitor.MakeRequestAsync(name, action).ConfigureAwait(false);

				// Update rate limit budget from successful response for pre-emptive pacing
				UpdateRateLimitFromApiInfo();

				ClearStatus();
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
						// GitHub returns 403 for both auth failures and rate limits
						// Check if this is a rate limit response before clearing credentials
						if (IsRateLimitResponse(e))
						{
							OnRateLimitExceeded(ParseRateLimitResetTime(e));
						}
						else
						{
							OnAuthenticationFailure();
						}
						break;
					case System.Net.HttpStatusCode.TooManyRequests:
						OnRateLimitExceeded(ParseRateLimitResetTime(e));
						break;
					default:
						throw;
				}
			}
			catch (HttpRequestException ex)
			{
				Log.Error($"{Name}: Connection error - {ex.Message}");
				SetStatus(ProviderStatus.Error, $"{Strings.ConnectionErrorMessage} {ex.Message}");
			}
		}
		finally
		{
			RequestSemaphore.Release();
		}
	}

	/// <summary>
	/// Updates the rate limit budget from the last API response.
	/// Uses Octokit's GetLastApiInfo() to retrieve rate limit headers.
	/// </summary>
	private void UpdateRateLimitFromApiInfo()
	{
		ApiInfo? apiInfo = GitHubClient.GetLastApiInfo();
		if (apiInfo?.RateLimit != null)
		{
			UpdateRateLimitBudget(
				apiInfo.RateLimit.Remaining,
				apiInfo.RateLimit.Limit,
				apiInfo.RateLimit.Reset);
		}
	}

	private static DateTimeOffset? ParseRateLimitResetTime(ApiException exception)
	{
		if (exception.HttpResponse?.Headers == null)
		{
			return null;
		}

		if (exception.HttpResponse.Headers.TryGetValue("X-RateLimit-Reset", out string? resetValue) &&
			long.TryParse(resetValue, CultureInfo.InvariantCulture, out long unixTimestamp))
		{
			return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
		}

		return null;
	}

	private static bool IsRateLimitResponse(ApiException exception)
	{
		if (exception.HttpResponse?.Headers == null)
		{
			return false;
		}

		// Check if X-RateLimit-Remaining is 0, indicating rate limit exceeded
		if (exception.HttpResponse.Headers.TryGetValue("X-RateLimit-Remaining", out string? remainingValue) &&
			int.TryParse(remainingValue, CultureInfo.InvariantCulture, out int remaining) &&
			remaining == 0)
		{
			return true;
		}

		// Also check for "rate limit" in the error message as a fallback
		return exception.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Re-runs a workflow run.
	/// </summary>
	/// <param name="run">The workflow run to re-run.</param>
	/// <returns>True if the operation was successful, false otherwise.</returns>
	internal async Task<bool> RerunWorkflowAsync(Run run)
	{
		if (!HasValidCredentials(run.Owner))
		{
			return false;
		}

		UpdateGitHubClientCredentials(run.Owner);
		try
		{
			await MakeGitHubRequestAsync($"{Name}/{run.Owner.Name}/{run.Repository.Name}/rerun/{run.Id}", async () => await GitHubRuns.Rerun(run.Owner.Name, run.Repository.Name, long.Parse(run.Id, CultureInfo.InvariantCulture)).ConfigureAwait(false)).ConfigureAwait(false);
			return true;
		}
		catch (NotFoundException)
		{
			return false;
		}
		catch (ApiException)
		{
			return false;
		}
	}

	/// <summary>
	/// Cancels a running workflow.
	/// </summary>
	/// <param name="run">The workflow run to cancel.</param>
	/// <returns>True if the operation was successful, false otherwise.</returns>
	internal async Task<bool> CancelWorkflowAsync(Run run)
	{
		if (!HasValidCredentials(run.Owner))
		{
			return false;
		}

		UpdateGitHubClientCredentials(run.Owner);
		try
		{
			await MakeGitHubRequestAsync($"{Name}/{run.Owner.Name}/{run.Repository.Name}/cancel/{run.Id}", async () => await GitHubRuns.Cancel(run.Owner.Name, run.Repository.Name, long.Parse(run.Id, CultureInfo.InvariantCulture)).ConfigureAwait(false)).ConfigureAwait(false);
			return true;
		}
		catch (NotFoundException)
		{
			return false;
		}
		catch (ApiException)
		{
			return false;
		}
	}

	/// <summary>
	/// Triggers a workflow dispatch event.
	/// </summary>
	/// <param name="build">The workflow build to trigger.</param>
	/// <param name="branch">The branch to run the workflow on.</param>
	/// <returns>True if the operation was successful, false otherwise.</returns>
	internal async Task<bool> TriggerWorkflowAsync(Build build, BranchName branch)
	{
		if (!HasValidCredentials(build.Owner))
		{
			return false;
		}

		UpdateGitHubClientCredentials(build.Owner);
		try
		{
			await MakeGitHubRequestAsync($"{Name}/{build.Owner.Name}/{build.Repository.Name}/dispatch/{build.Name}", async () =>
			{
				CreateWorkflowDispatch createWorkflowDispatch = new(branch)
				{
					Inputs = new Dictionary<string, object>()
				};
				await GitHubActions.Workflows.CreateDispatch(build.Owner.Name, build.Repository.Name, long.Parse(build.Id, CultureInfo.InvariantCulture), createWorkflowDispatch).ConfigureAwait(false);
			}).ConfigureAwait(false);
			return true;
		}
		catch (NotFoundException)
		{
			return false;
		}
		catch (ApiException)
		{
			return false;
		}
	}
}
