// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor;

using System.Diagnostics;

internal sealed class RunSync
{
	internal Run Run { get; set; } = new();
	private Stopwatch UpdateTimer { get; } = new Stopwatch();
	private int UpdateIntervalCurrent { get; set; } = UpdateIntervalMin;
	private const int UpdateIntervalMin = 10;
	private const int UpdateIntervalMax = 60;

	/// <summary>
	/// Returns true if the run no longer exists in its parent build's runs collection.
	/// This happens when the run is deleted or the build/repository is removed.
	/// </summary>
	internal bool IsOrphaned => !Run.Build.Runs.ContainsKey(Run.Id);

	/// <summary>
	/// Gets the priority of this run for API request scheduling.
	/// Running runs are always high priority since they're actively changing.
	/// </summary>
	internal RequestPriority Priority => Run.IsOngoing ? RequestPriority.High : RequestPriority.Low;

	internal bool ShouldUpdate => !IsOrphaned && Run.IsOngoing && UpdateTimer.Elapsed.TotalSeconds >= UpdateIntervalCurrent;

	internal RunSync() => UpdateTimer.Start();

	internal async Task UpdateAsync()
	{
		// Restart the timer whether or not the update succeeds, so a run whose update keeps failing
		// is retried on its interval rather than back to back (ktsu-dev/BuildMonitor#299). A run that
		// is no longer ongoing never updates again, so restarting its timer changes nothing.
		try
		{
			_ = await SyncGuard.RunAsync(UpdateRunAsync, $"RunSync: {Run.Owner.Name}/{Run.Repository.Name}/{Run.Build.Name}/{Run.Name}").ConfigureAwait(false);
		}
		finally
		{
			UpdateTimer.Restart();
		}
	}

	private async Task UpdateRunAsync()
	{
		await Run.Owner.BuildProvider.UpdateRunAsync(Run).ConfigureAwait(false);

		if (Run.IsOngoing)
		{
			UpdateIntervalCurrent = (int)Run.CalculateETA().TotalSeconds;
			UpdateIntervalCurrent = Math.Clamp(UpdateIntervalCurrent, UpdateIntervalMin, UpdateIntervalMax);
		}
	}
}
