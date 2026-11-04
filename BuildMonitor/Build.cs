// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;
using ktsu.Semantics.Strings;

internal sealed record class BuildName : SemanticString<BuildName> { }
internal sealed record class BuildId : SemanticString<BuildId> { }

internal sealed class Build
{
	private readonly Lock _updateLock = new();

	public BuildName Name { get; set; } = new();
	public BuildId Id { get; set; } = new();
	public Owner Owner { get; set; } = new();
	public Repository Repository { get; set; } = new();
	public bool Enabled { get; set; }
	public ConcurrentDictionary<RunId, Run> Runs { get; init; } = [];
	public DateTimeOffset LastStarted { get; set; }
	public DateTimeOffset LastUpdated { get; set; }
	public TimeSpan LastDuration => LastUpdated - LastStarted;
	public RunStatus LastStatus { get; set; }
	internal bool IsOngoing => !Runs.IsEmpty && LastStatus is RunStatus.Pending or RunStatus.Running;

	/// <summary>
	/// Gets a value indicating whether any of the build's runs, on any branch, is still pending or running.
	/// <see cref="IsOngoing"/> reads only the most recently started run, so an earlier run on another
	/// branch that is still going was missed once a later one finished (ktsu-dev/BuildMonitor#392).
	/// </summary>
	internal bool HasOngoingRun => IsOngoing || Runs.Values.Any(run => run.IsOngoing);

	/// <summary>
	/// Returns whether the newest run on any branch failed and was last updated after <paramref name="since"/>.
	/// </summary>
	/// <param name="since">The earliest update time that counts as recent.</param>
	/// <returns><see langword="true"/> if some branch's newest run is a recent failure.</returns>
	internal bool HasRecentBranchFailure(DateTimeOffset since)
	{
		if (LastStatus == RunStatus.Failure && LastUpdated > since)
		{
			return true;
		}

		return Runs.Values
			.GroupBy(run => run.Branch)
			.Select(branch => branch.MaxBy(run => run.Started)!)
			.Any(newest => newest.Status == RunStatus.Failure && newest.LastUpdated > since);
	}

	/// <summary>
	/// Calculates the estimated duration for this build using sophisticated statistical methods.
	/// Uses all successful runs across all branches.
	/// </summary>
	/// <returns>The estimated duration, or TimeSpan.Zero if insufficient data.</returns>
	internal TimeSpan CalculateEstimatedDuration() => DurationEstimator.EstimateDuration(this);

	/// <summary>
	/// Calculates the estimated duration for this build on a specific branch.
	/// Falls back to overall build estimation if branch has insufficient data.
	/// </summary>
	/// <param name="branch">The branch to estimate for.</param>
	/// <returns>The estimated duration, or TimeSpan.Zero if insufficient data.</returns>
	internal TimeSpan CalculateEstimatedDuration(BranchName branch) => DurationEstimator.EstimateDuration(this, branch);

	/// <summary>
	/// How many of a build's newest runs are always kept, whatever their branch or status.
	/// </summary>
	internal const int RetainedNewestRuns = 50;

	/// <summary>
	/// How long a branch keeps its newest run, and so its table row, after that run started.
	/// </summary>
	internal static readonly TimeSpan BranchRetention = TimeSpan.FromDays(30);

	/// <summary>
	/// Drops runs that nothing needs any more, so <see cref="Runs"/> and the app data file stay bounded
	/// while a busy workflow is polled for months (ktsu-dev/BuildMonitor#313).
	/// </summary>
	/// <param name="now">The current instant, which the branch retention window is measured back from.</param>
	/// <returns>The number of runs removed.</returns>
	/// <remarks>
	/// A run is kept when any of these holds:
	/// <list type="bullet">
	/// <item>it is still pending or running;</item>
	/// <item>it is one of the build's <see cref="RetainedNewestRuns"/> newest runs;</item>
	/// <item>it is one of the newest <see cref="DurationEstimator.MaxSamplesToConsider"/> successful runs,
	/// which is every sample the duration estimator reads;</item>
	/// <item>it is the newest run on a branch that ran within <see cref="BranchRetention"/>.</item>
	/// </list>
	/// A branch none of whose runs is kept disappears from the table.
	/// </remarks>
	internal int PruneRuns(DateTimeOffset now)
	{
		List<Run> runs = [.. Runs.Values];
		if (runs.Count <= RetainedNewestRuns)
		{
			return 0;
		}

		HashSet<RunId> kept = [.. runs.Where(run => run.IsOngoing).Select(run => run.Id)];
		kept.UnionWith(runs
			.OrderByDescending(run => run.Started)
			.Take(RetainedNewestRuns)
			.Select(run => run.Id));
		kept.UnionWith(runs
			.Where(run => run.Status == RunStatus.Success)
			.OrderByDescending(run => run.Started)
			.Take(DurationEstimator.MaxSamplesToConsider)
			.Select(run => run.Id));
		kept.UnionWith(runs
			.GroupBy(run => run.Branch)
			.Select(branch => branch.MaxBy(run => run.Started)!)
			.Where(newest => now - newest.Started <= BranchRetention)
			.Select(newest => newest.Id));

		int removed = 0;
		foreach (Run run in runs.Where(run => !kept.Contains(run.Id)))
		{
			if (Runs.TryRemove(run.Id, out _))
			{
				removed++;
			}
		}

		return removed;
	}

	internal TimeSpan CalculateETA()
	{
		TimeSpan estimate = CalculateEstimatedDuration();
		TimeSpan duration = IsOngoing ? Clock.UtcNow - LastStarted : LastDuration;
		return duration < estimate ? estimate - duration : TimeSpan.Zero;
	}

	internal Run CreateRun(RunName name) => CreateRun(name, name.As<RunId>());
	internal Run CreateRun(RunName name, RunId id)
	{
		return new()
		{
			Name = name,
			Id = id,
			Build = this,
			Repository = Repository,
			Owner = Owner,
			Enabled = true
		};
	}

	internal void UpdateFromRun(Run run)
	{
		lock (_updateLock)
		{
			// Use Started time to track the most recently initiated run,
			// not LastUpdated which can change when older runs are re-fetched
			if (run.Started > LastStarted)
			{
				LastStarted = run.Started;
				LastUpdated = run.LastUpdated;
				LastStatus = run.Status;
			}
			else if (run.Started == LastStarted && (run.LastUpdated > LastUpdated || (IsOngoing && !run.IsOngoing)))
			{
				// Same run being updated with newer data. A run that has finished is always taken
				// over an ongoing one, because a provider may stamp an in-progress poll with the
				// local clock and the completion with the server's finish time, which can be earlier.
				LastUpdated = run.LastUpdated;
				LastStatus = run.Status;
			}
		}
	}
}
