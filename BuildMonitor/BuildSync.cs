// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Diagnostics;

internal sealed class BuildSync
{
	internal Build Build { get; set; } = new();
	private Stopwatch UpdateTimer { get; } = new Stopwatch();

	private const int UpdateInterval = 60;

	/// <summary>
	/// Returns true if the build no longer exists in its parent repository's builds collection.
	/// This happens when the build/workflow is deleted or the repository is removed.
	/// </summary>
	internal bool IsOrphaned => !Build.Repository.Builds.ContainsKey(Build.Id);

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
