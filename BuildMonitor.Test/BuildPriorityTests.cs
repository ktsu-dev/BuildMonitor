// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using ktsu.Semantics.Strings;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers how a build's polling priority reads its runs. It used to read only the most recently started
/// run across every branch, so a long run on main still going after a later PR run had finished dropped
/// the build to Low, and main then failing never reached the recent-failure tier
/// (ktsu-dev/BuildMonitor#392).
/// </summary>
[TestClass]
public sealed class BuildPriorityTests
{
	private static readonly DateTimeOffset T0 = new(2026, 11, 3, 6, 0, 0, TimeSpan.Zero);

	[TestCleanup]
	public void Cleanup() => Clock.Reset();

	private static Run AddRun(Build build, string id, string branch, DateTimeOffset started, RunStatus status, DateTimeOffset lastUpdated)
	{
		Run run = build.CreateRun(id.As<RunName>());
		run.Branch = branch.As<BranchName>();
		run.Started = started;
		run.Status = status;
		run.LastUpdated = lastUpdated;
		build.Runs[run.Id] = run;
		build.UpdateFromRun(run);
		return run;
	}

	[TestMethod]
	public void ARunStillGoingOnAnotherBranchKeepsTheBuildHigh()
	{
		// Arrange
		Clock.UtcNowOverride = () => T0.AddMinutes(13);
		Build build = new();
		AddRun(build, "1", "main", T0, RunStatus.Running, T0.AddMinutes(12));
		AddRun(build, "2", "feature", T0.AddMinutes(10), RunStatus.Success, T0.AddMinutes(12));

		// Act
		RequestPriority priority = new BuildSync { Build = build }.Priority;

		// Assert
		Assert.AreEqual(RequestPriority.High, priority, "main is still running, so the build should be polled at the running rate");
	}

	[TestMethod]
	public void AFailureOnAnotherBranchIsARecentFailure()
	{
		// Arrange
		Clock.UtcNowOverride = () => T0.AddMinutes(21);
		Build build = new();
		Run main = AddRun(build, "1", "main", T0, RunStatus.Running, T0.AddMinutes(12));
		AddRun(build, "2", "feature", T0.AddMinutes(10), RunStatus.Success, T0.AddMinutes(12));

		// Act
		main.Status = RunStatus.Failure;
		main.LastUpdated = T0.AddMinutes(20);
		build.UpdateFromRun(main);
		RequestPriority priority = new BuildSync { Build = build }.Priority;

		// Assert
		Assert.AreEqual(RequestPriority.Medium, priority, "main's newest run failed a minute ago");
	}

	[TestMethod]
	public void AnOlderFailureSupersededOnItsBranchIsNotRecent()
	{
		// Arrange
		Clock.UtcNowOverride = () => T0.AddMinutes(30);
		Build build = new();
		AddRun(build, "1", "main", T0, RunStatus.Failure, T0.AddMinutes(5));
		AddRun(build, "2", "main", T0.AddMinutes(10), RunStatus.Success, T0.AddMinutes(15));
		AddRun(build, "3", "feature", T0.AddMinutes(20), RunStatus.Success, T0.AddMinutes(25));

		// Act
		RequestPriority priority = new BuildSync { Build = build }.Priority;

		// Assert
		Assert.AreEqual(RequestPriority.Low, priority, "main's failure was fixed by a later run on main");
	}

	[TestMethod]
	public void AFailureOverAnHourOldIsLow()
	{
		// Arrange
		Clock.UtcNowOverride = () => T0.AddHours(3);
		Build build = new();
		AddRun(build, "1", "main", T0, RunStatus.Failure, T0.AddMinutes(20));
		AddRun(build, "2", "feature", T0.AddMinutes(10), RunStatus.Success, T0.AddMinutes(12));

		// Act
		RequestPriority priority = new BuildSync { Build = build }.Priority;

		// Assert
		Assert.AreEqual(RequestPriority.Low, priority);
	}

	[TestMethod]
	public void ARunAwaitingApprovalIsNotARecentFailure()
	{
		// Arrange
		Clock.UtcNowOverride = () => T0.AddHours(3);
		Build build = new();
		AddRun(build, "1", "main", T0, RunStatus.Success, T0.AddMinutes(5));
		RunStatus awaitingApproval = GitHub.MapWorkflowRunStatus(Octokit.WorkflowRunStatus.Completed, Octokit.WorkflowRunConclusion.ActionRequired);
		AddRun(build, "2", "patch-1", T0.AddHours(3).AddMinutes(-2), awaitingApproval, T0.AddHours(3).AddMinutes(-2));

		// Act
		RequestPriority priority = new BuildSync { Build = build }.Priority;

		// Assert
		Assert.AreEqual(RequestPriority.Low, priority, "a fork PR waiting for approval has not failed (ktsu-dev/BuildMonitor#405)");
	}
}
