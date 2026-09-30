// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using ktsu.Semantics.Strings;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers what happens when a provider update throws — a persistent GitHub 5xx, for example. The
/// sync's timer used to be left running, so <c>ShouldUpdate</c> stayed true and the update loop
/// polled the same item again back to back instead of on its interval, and the exception faulted
/// the whole batch (ktsu-dev/BuildMonitor#299).
/// </summary>
[TestClass]
public sealed class SyncFailureTests
{
	/// <summary>
	/// Long enough for a timer that was not restarted to show a visibly shorter time remaining.
	/// </summary>
	private static readonly TimeSpan Elapse = TimeSpan.FromMilliseconds(1100);

	/// <summary>
	/// A provider whose build and run updates throw for the items named in <see cref="Failing"/>
	/// and record the rest.
	/// </summary>
	private sealed class FailingProvider : BuildProvider
	{
		internal override BuildProviderName Name { get; } = "Failing".As<BuildProviderName>();

		internal HashSet<string> Failing { get; } = [];

		internal List<string> Updated { get; } = [];

		internal override Task UpdateRepositoriesAsync(Owner owner) => Task.CompletedTask;
		internal override Task UpdateBuildsAsync(Repository repository) => Task.CompletedTask;
		internal override Task UpdateBuildAsync(Build build) => Record(build.Name);
		internal override Task UpdateRunAsync(Run run) => Record(run.Name);

		private Task Record(string name)
		{
			if (Failing.Contains(name))
			{
				throw new HttpRequestException("502 Bad Gateway");
			}

			lock (Updated)
			{
				Updated.Add(name);
			}

			return Task.CompletedTask;
		}
	}

	private static Build AddBuild(Repository repository, string name)
	{
		Build build = repository.CreateBuild(name.As<BuildName>());
		Assert.IsTrue(repository.Builds.TryAdd(build.Id, build));
		return build;
	}

	private static Repository CreateRepository(FailingProvider provider)
	{
		Owner owner = provider.CreateOwner("owner".As<OwnerName>());
		return owner.CreateRepository("repo".As<RepositoryName>());
	}

	[TestMethod]
	public async Task AFailedBuildUpdateStillRestartsItsTimer()
	{
		// Arrange
		FailingProvider provider = new();
		provider.Failing.Add("broken");
		BuildSync sync = new() { Build = AddBuild(CreateRepository(provider), "broken") };
		await Task.Delay(Elapse, TestContext.CancellationToken).ConfigureAwait(false);
		TimeSpan remainingBefore = sync.TimeRemaining;

		// Act
		await sync.UpdateAsync().ConfigureAwait(false);

		// Assert
		Assert.IsGreaterThan(remainingBefore, sync.TimeRemaining, "A failed update should restart the timer, so the build waits out its interval before the next poll");
		Assert.IsFalse(sync.ShouldUpdate);
	}

	[TestMethod]
	public async Task AFailedBuildUpdateDoesNotStopTheOthersInTheBatch()
	{
		// Arrange
		FailingProvider provider = new();
		provider.Failing.Add("broken");
		Repository repository = CreateRepository(provider);
		BuildSync broken = new() { Build = AddBuild(repository, "broken") };
		BuildSync healthy = new() { Build = AddBuild(repository, "healthy") };

		// Act
		Task batch = Task.WhenAll(broken.UpdateAsync(), healthy.UpdateAsync());
		await batch.ConfigureAwait(false);

		// Assert
		Assert.IsFalse(batch.IsFaulted, "One failing build should not fault the batch, which skips the timer restarts and pruning after it");
		Assert.HasCount(1, provider.Updated);
		Assert.AreEqual("healthy", provider.Updated[0], "The healthy build should still be polled");
	}

	/// <summary>
	/// Gets or sets the test context MSTest injects, used for its cancellation token.
	/// </summary>
	public TestContext TestContext { get; set; } = null!;
}
