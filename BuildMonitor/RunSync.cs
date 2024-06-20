// Ignore Spelling: App

namespace ktsu.io.BuildMonitor;

using System.Diagnostics;

internal class RunSync
{
	internal Run Run { get; set; } = new();
	private Stopwatch SyncTimer { get; } = new Stopwatch();
	private int SyncIntervalCurrent { get; set; } = SyncIntervalMin;
	private const int SyncIntervalMin = 10;
	private const int SyncIntervalMax = 60;

	internal bool ShouldSync => SyncTimer.Elapsed.TotalSeconds >= SyncIntervalCurrent;

	internal RunSync() => SyncTimer.Start();

	internal async Task Sync()
	{
		await Run.Owner.BuildProvider.SyncRunAsync(Run);

		if (Run.IsOngoing)
		{
			SyncIntervalCurrent = (int)Run.CalculateETA().TotalSeconds;
			SyncIntervalCurrent = Math.Clamp(SyncIntervalCurrent, SyncIntervalMin, SyncIntervalMax);
			SyncTimer.Restart();
		}
	}
}
