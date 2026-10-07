// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor;

using System.Diagnostics;

/// <summary>
/// Priority levels for API requests. Lower values = higher priority.
/// </summary>
internal enum RequestPriority
{
	/// <summary>Running/in-progress builds - users care most about these.</summary>
	High = 0,
	/// <summary>Recent failures or builds with recent activity.</summary>
	Medium = 1,
	/// <summary>Completed/successful builds, discovery operations.</summary>
	Low = 2,
}

internal sealed class BuildSync
{
	internal Build Build { get; set; } = new();
	private Stopwatch UpdateTimer { get; } = new Stopwatch();

	// Update intervals based on priority (in seconds)
	private const int UpdateIntervalHigh = 30;
	private const int UpdateIntervalMedium = 60;
	private const int UpdateIntervalLow = 120;

	/// <summary>
	/// Set by <see cref="ForceUpdate"/> to poll the build on the next tick regardless of its interval.
	/// </summary>
	private volatile bool forceUpdate;

	/// <summary>
	/// Returns true if the build no longer exists in its parent repository's builds collection.
	/// This happens when the build/workflow is deleted or the repository is removed.
	/// </summary>
	internal bool IsOrphaned => !Build.Repository.Builds.ContainsKey(Build.Id);

	/// <summary>
	/// Gets the priority of this build for API request scheduling.
	/// </summary>
	internal RequestPriority Priority
	{
		get
		{
			// Running builds are highest priority
			if (Build.IsOngoing)
			{
				return RequestPriority.High;
			}

			// Recent failures are medium priority (within last hour)
			if (Build.LastStatus == RunStatus.Failure &&
				Build.LastUpdated > DateTimeOffset.UtcNow.AddHours(-1))
			{
				return RequestPriority.Medium;
			}

			// Everything else is low priority
			return RequestPriority.Low;
		}
	}

	/// <summary>
	/// Gets the update interval based on the build's priority.
	/// </summary>
	private int UpdateInterval => Priority switch
	{
		RequestPriority.High => UpdateIntervalHigh,
		RequestPriority.Medium => UpdateIntervalMedium,
		_ => UpdateIntervalLow,
	};

	internal TimeSpan TimeRemaining => forceUpdate
		? TimeSpan.Zero
		: TimeSpan.FromSeconds(Math.Max(0, UpdateInterval - UpdateTimer.Elapsed.TotalSeconds));

	internal double UpdateProgress => forceUpdate ? 1 : Math.Clamp(UpdateTimer.Elapsed.TotalSeconds / UpdateInterval, 0, 1);

	internal bool ShouldUpdate => !IsOrphaned && (forceUpdate || UpdateTimer.Elapsed.TotalSeconds >= UpdateInterval);

	internal BuildSync() => UpdateTimer.Start();

	/// <summary>
	/// Queues the build to be polled on the next tick, however much of its interval is left. Restarting
	/// the timer instead pushed the next poll a full interval away (ktsu-dev/BuildMonitor#303).
	/// </summary>
	internal void ForceUpdate() => forceUpdate = true;

	internal async Task UpdateAsync()
	{
		// Clear the request before polling, so a refresh asked for while this update is in flight still
		// gets its own poll afterwards.
		forceUpdate = false;

		// Restart the timer whether or not the update succeeds. A failed update that left it running
		// kept ShouldUpdate true, so the build was polled again back to back (ktsu-dev/BuildMonitor#299).
		try
		{
			_ = await SyncGuard.RunAsync(UpdateBuildAsync, $"BuildSync: {Build.Owner.Name}/{Build.Repository.Name}/{Build.Name}").ConfigureAwait(false);
		}
		finally
		{
			UpdateTimer.Restart();
		}
	}

	private async Task UpdateBuildAsync()
	{
		int runsBefore = Build.Runs.Count;
		await Build.Owner.BuildProvider.UpdateBuildAsync(Build).ConfigureAwait(false);
		int runsAfter = Build.Runs.Count;

		if (runsAfter != runsBefore)
		{
			Log.Info($"BuildSync: {Build.Owner.Name}/{Build.Repository.Name}/{Build.Name} runs changed: {runsBefore} -> {runsAfter}");
		}
		else
		{
			Log.Debug($"BuildSync: {Build.Owner.Name}/{Build.Repository.Name}/{Build.Name} polled, {runsAfter} runs (no change)");
		}

		foreach ((RunId? runId, Run? run) in Build.Runs)
		{
			_ = BuildMonitor.RunSyncCollection.TryAdd(runId, new()
			{
				Run = run,
			});
		}
	}
}
