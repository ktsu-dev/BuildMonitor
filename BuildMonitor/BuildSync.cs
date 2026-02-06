// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

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

	internal TimeSpan TimeRemaining => TimeSpan.FromSeconds(
		Math.Max(0, UpdateInterval - UpdateTimer.Elapsed.TotalSeconds));

	internal double UpdateProgress => Math.Clamp(UpdateTimer.Elapsed.TotalSeconds / UpdateInterval, 0, 1);

	internal bool ShouldUpdate => !IsOrphaned && UpdateTimer.Elapsed.TotalSeconds >= UpdateInterval;

	internal BuildSync() => UpdateTimer.Start();

	internal void ResetTimer() => UpdateTimer.Restart();

	internal async Task UpdateAsync()
	{
		await Build.Owner.BuildProvider.UpdateBuildAsync(Build).ConfigureAwait(false);

		foreach ((RunId? runId, Run? run) in Build.Runs)
		{
			_ = BuildMonitor.RunSyncCollection.TryAdd(runId, new()
			{
				Run = run,
			});
		}

		UpdateTimer.Restart();
	}
}
