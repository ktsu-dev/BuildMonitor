// Ignore Spelling: App

namespace ktsu.io.BuildMonitor;

using System.Diagnostics;

internal class BuildSync
{
	internal Build Build { get; set; } = new();
	private Stopwatch SyncTimer { get; } = new Stopwatch();

	private const int SyncInterval = 60;

	internal bool ShouldSync => SyncTimer.Elapsed.TotalSeconds >= SyncInterval;

	internal BuildSync() => SyncTimer.Start();

	internal async Task Sync()
	{
		await Build.Owner.BuildProvider.SyncRunsAsync(Build);

		lock (BuildMonitor.SyncLock)
		{
			foreach (var (runId, run) in Build.Runs)
			{
				_ = BuildMonitor.RunSyncCollection.TryAdd(runId, new()
				{
					Run = run,
				});
			}
		}
		SyncTimer.Restart();
	}
}
