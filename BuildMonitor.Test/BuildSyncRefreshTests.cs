// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using ktsu.Semantics.Strings;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers "Refresh Build Data" and the refresh after a re-run, cancel or trigger. It used to restart
/// the build's timer, which pushed the next poll a full interval away instead of forcing it
/// (ktsu-dev/BuildMonitor#303).
/// </summary>
[TestClass]
public sealed class BuildSyncRefreshTests
{
	/// <summary>
	/// A provider that records the builds it was asked to update.
	/// </summary>
	private sealed class RecordingProvider : BuildProvider
	{
		internal override BuildProviderName Name { get; } = "Recording".As<BuildProviderName>();

		internal int BuildUpdates { get; private set; }

		internal override Task UpdateRepositoriesAsync(Owner owner) => Task.CompletedTask;
		internal override Task UpdateBuildsAsync(Repository repository) => Task.CompletedTask;
		internal override Task UpdateRunAsync(Run run) => Task.CompletedTask;

		internal override Task UpdateBuildAsync(Build build)
		{
			BuildUpdates++;
			return Task.CompletedTask;
		}
	}

	private static BuildSync CreateSync(RecordingProvider provider)
	{
		Owner owner = provider.CreateOwner("owner".As<OwnerName>());
		Repository repository = owner.CreateRepository("repo".As<RepositoryName>());
		Build build = repository.CreateBuild("build".As<BuildName>());
		Assert.IsTrue(repository.Builds.TryAdd(build.Id, build));
		return new() { Build = build };
	}

	[TestMethod]
	public void ForceUpdateMakesTheBuildDueOnTheNextTick()
	{
		// Arrange
		BuildSync sync = CreateSync(new());
		Assert.IsFalse(sync.ShouldUpdate, "A new low-priority build should be a full interval from its first poll");

		// Act
		sync.ForceUpdate();

		// Assert
		Assert.IsTrue(sync.ShouldUpdate, "A refresh should make the build due now, not a full interval later");
		Assert.AreEqual(TimeSpan.Zero, sync.TimeRemaining);
		Assert.AreEqual(1, sync.UpdateProgress, "The progress bar should show the build as due");
	}

	[TestMethod]
	public void RefreshBuildDataForcesTheTrackedBuildsUpdate()
	{
		// Arrange
		BuildSync sync = CreateSync(new());
		Assert.IsTrue(BuildMonitor.BuildSyncCollection.TryAdd(sync.Build.Id, sync));

		try
		{
			// Act
			BuildMonitor.RefreshBuildData(sync.Build);

			// Assert
			Assert.IsTrue(sync.ShouldUpdate, "Refresh Build Data should queue the build for its next tick");
		}
		finally
		{
			_ = BuildMonitor.BuildSyncCollection.TryRemove(sync.Build.Id, out _);
		}
	}

	[TestMethod]
	public async Task NormalPacingResumesAfterTheForcedUpdate()
	{
		// Arrange
		RecordingProvider provider = new();
		BuildSync sync = CreateSync(provider);
		sync.ForceUpdate();

		// Act
		await sync.UpdateAsync().ConfigureAwait(false);

		// Assert
		Assert.AreEqual(1, provider.BuildUpdates);
		Assert.IsFalse(sync.ShouldUpdate, "Once the forced poll has run, the build should wait out its interval again");
		Assert.IsGreaterThan(TimeSpan.FromSeconds(100), sync.TimeRemaining);
	}
}
