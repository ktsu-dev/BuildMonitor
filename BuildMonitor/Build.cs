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
	/// It is read from the runs alone, not from the cached <see cref="LastStatus"/>: a saved status can
	/// name a run that has since been deleted (ktsu-dev/BuildMonitor#406).
	/// </summary>
	internal bool HasOngoingRun => Runs.Values.Any(run => run.IsOngoing);

	/// <summary>
	/// Returns whether the newest run on any branch failed and was last updated after <paramref name="since"/>.
	/// </summary>
	/// <param name="since">The earliest update time that counts as recent.</param>
	/// <returns><see langword="true"/> if some branch's newest run is a recent failure.</returns>
	internal bool HasRecentBranchFailure(DateTimeOffset since)
	{
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

	/// <summary>
	/// Removes a run, such as one the provider reports as deleted, and recomputes <see cref="LastStarted"/>,
	/// <see cref="LastUpdated"/> and <see cref="LastStatus"/> from the runs that remain. Without that, a
	/// deleted run that was still running stayed the build's newest run, because only a later run replaces
	/// it (ktsu-dev/BuildMonitor#406).
	/// </summary>
	/// <param name="id">The id of the run to remove.</param>
	/// <returns><see langword="true"/> if the run was removed.</returns>
	internal bool RemoveRun(RunId id)
	{
		lock (_updateLock)
		{
			if (!Runs.TryRemove(id, out _))
			{
				return false;
			}

			Run? newest = Runs.Values
				.OrderByDescending(run => run.Started)
				.ThenByDescending(run => run.LastUpdated)
				.FirstOrDefault();

			LastStarted = newest?.Started ?? default;
			LastUpdated = newest?.LastUpdated ?? default;
			LastStatus = newest?.Status ?? default;
			return true;
		}
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
