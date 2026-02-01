// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

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
		await Run.Owner.BuildProvider.UpdateRunAsync(Run).ConfigureAwait(false);

		if (Run.IsOngoing)
		{
			UpdateIntervalCurrent = (int)Run.CalculateETA().TotalSeconds;
			UpdateIntervalCurrent = Math.Clamp(UpdateIntervalCurrent, UpdateIntervalMin, UpdateIntervalMax);
			UpdateTimer.Restart();
		}
	}
}
