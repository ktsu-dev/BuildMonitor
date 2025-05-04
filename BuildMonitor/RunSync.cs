// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Diagnostics;

internal class RunSync
{
	internal Run Run { get; set; } = new();
	private Stopwatch UpdateTimer { get; } = new Stopwatch();
	private int UpdateIntervalCurrent { get; set; } = UpdateIntervalMin;
	private const int UpdateIntervalMin = 10;
	private const int UpdateIntervalMax = 60;

	internal bool ShouldUpdate => Run.IsOngoing && UpdateTimer.Elapsed.TotalSeconds >= UpdateIntervalCurrent;

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
