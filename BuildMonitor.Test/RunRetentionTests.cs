// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using ktsu.Semantics.Strings;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers <see cref="Build.PruneRuns"/>. Every poll adds the newest runs a provider reports and nothing
/// removed them, so every branch that ever ran kept a table row forever and the app data file grew
/// without bound (ktsu-dev/BuildMonitor#313).
/// </summary>
[TestClass]
public sealed class RunRetentionTests
{
	private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

	private static Build NewBuild() => new() { Name = "ci".As<BuildName>(), Id = "1".As<BuildId>() };

	private static Run AddRun(Build build, string id, string branch, DateTimeOffset started, RunStatus status = RunStatus.Success)
	{
		Run run = new()
		{
			Id = id.As<RunId>(),
			Name = id.As<RunName>(),
			Branch = branch.As<BranchName>(),
			Status = status,
			Started = started,
			LastUpdated = started.AddMinutes(5),
			Build = build,
		};
		build.Runs[run.Id] = run;
		return run;
	}

	/// <summary>
	/// Adds runs on main, an hour apart, the newest starting at <paramref name="newest"/>.
	/// </summary>
	private static List<Run> AddMainRuns(Build build, int count, DateTimeOffset newest, RunStatus status = RunStatus.Success) =>
		[.. Enumerable.Range(0, count).Select(i => AddRun(build, $"main-{status}-{i}", "main", newest.AddHours(-i), status))];

	[TestMethod]
	public void MoreRunsThanTheCapAreTrimmedToTheNewest()
	{
		// Arrange
		Build build = NewBuild();
		List<Run> runs = AddMainRuns(build, Build.RetainedNewestRuns + 30, Now.AddDays(-60));

		// Act
		int removed = build.PruneRuns(Now);

		// Assert
		Assert.AreEqual(30, removed);
		Assert.HasCount(Build.RetainedNewestRuns, build.Runs);
		CollectionAssert.AreEquivalent(
			runs.Take(Build.RetainedNewestRuns).Select(run => run.Id).ToList(),
			build.Runs.Keys.ToList(),
			"The newest runs should be the ones kept");
	}

	[TestMethod]
	public void ABuildUnderTheCapKeepsEveryRun()
	{
		// Arrange
		Build build = NewBuild();
		_ = AddMainRuns(build, Build.RetainedNewestRuns, Now.AddDays(-400));

		// Act
		int removed = build.PruneRuns(Now);

		// Assert
		Assert.AreEqual(0, removed);
		Assert.HasCount(Build.RetainedNewestRuns, build.Runs);
	}

	[TestMethod]
	public void AnOldBranchOutsideTheNewestRunsLosesItsRow()
	{
		// Arrange - a dependabot branch ran once, long ago, and has since been merged and deleted
		Build build = NewBuild();
		_ = AddRun(build, "old-branch", "dependabot/nuget/Foo-1.2.3", Now - Build.BranchRetention - TimeSpan.FromDays(1));
		_ = AddMainRuns(build, Build.RetainedNewestRuns, Now);

		// Act
		_ = build.PruneRuns(Now);

		// Assert
		Assert.IsFalse(build.Runs.ContainsKey("old-branch".As<RunId>()));
		Assert.IsFalse(build.Runs.Values.Any(run => run.Branch == "dependabot/nuget/Foo-1.2.3".As<BranchName>()), "The branch should no longer have a row");
	}

	[TestMethod]
	public void ABranchThatRanWithinTheRetentionWindowKeepsItsNewestRun()
	{
		// Arrange
		Build build = NewBuild();
		Run olderFeatureRun = AddRun(build, "feature-old", "feature", Now.AddDays(-3));
		Run newestFeatureRun = AddRun(build, "feature-new", "feature", Now.AddDays(-2));
		_ = AddMainRuns(build, Build.RetainedNewestRuns, Now);

		// Act
		_ = build.PruneRuns(Now);

		// Assert
		Assert.IsTrue(build.Runs.ContainsKey(newestFeatureRun.Id), "The branch's newest run keeps its row");
		Assert.IsFalse(build.Runs.ContainsKey(olderFeatureRun.Id), "Only the branch's newest run is kept for it");
	}

	[TestMethod]
	public void AnOngoingRunIsNeverDropped()
	{
		// Arrange - a run queued long ago that is still waiting
		Build build = NewBuild();
		Run stuck = AddRun(build, "stuck", "old-branch", Now.AddDays(-90), RunStatus.Pending);
		_ = AddMainRuns(build, Build.RetainedNewestRuns + 10, Now);

		// Act
		_ = build.PruneRuns(Now);

		// Assert
		Assert.IsTrue(build.Runs.ContainsKey(stuck.Id));
	}

	[TestMethod]
	public void TheEstimatorKeepsItsFullSampleWhenRecentRunsFailed()
	{
		// Arrange - every recent run failed, so the successful ones the estimator reads are all older
		Build build = NewBuild();
		_ = AddMainRuns(build, Build.RetainedNewestRuns, Now, RunStatus.Failure);
		List<Run> successes = AddMainRuns(build, DurationEstimator.MaxSamplesToConsider + 5, Now.AddDays(-30), RunStatus.Success);
		TimeSpan estimateBefore = DurationEstimator.EstimateDuration(build);

		// Act
		_ = build.PruneRuns(Now);

		// Assert
		Assert.AreEqual(Build.RetainedNewestRuns + DurationEstimator.MaxSamplesToConsider, build.Runs.Count);
		foreach (Run success in successes.Take(DurationEstimator.MaxSamplesToConsider))
		{
			Assert.IsTrue(build.Runs.ContainsKey(success.Id), $"Successful run {success.Id} is an estimator sample and should be kept");
		}

		Assert.AreEqual(estimateBefore, DurationEstimator.EstimateDuration(build));
	}

	[TestMethod]
	public void RepeatedPollsOfABusyWorkflowStayBounded()
	{
		// Arrange - a poll adds ten new runs on fresh branches, and the build is pruned after each one
		Build build = NewBuild();
		int maxSeen = 0;

		// Act - a year of daily polls
		for (int day = 0; day < 365; day++)
		{
			DateTimeOffset pollTime = Now.AddDays(day);
			for (int i = 0; i < 10; i++)
			{
				_ = AddRun(build, $"run-{day}-{i}", $"branch-{day}-{i}", pollTime.AddMinutes(-i));
			}

			_ = build.PruneRuns(pollTime);
			maxSeen = Math.Max(maxSeen, build.Runs.Count);
		}

		// Assert - at most the newest runs plus one per branch that ran within the window
		int bound = Build.RetainedNewestRuns + (((int)Build.BranchRetention.TotalDays + 1) * 10);
		Assert.IsLessThanOrEqualTo(bound, maxSeen, $"Runs should stay bounded, but reached {maxSeen}");
	}
}
