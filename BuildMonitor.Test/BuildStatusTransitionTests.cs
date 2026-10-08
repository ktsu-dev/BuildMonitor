// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using ktsu.Semantics.Strings;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers how a build takes the status of its latest run. Azure DevOps stamps an in-progress run with
/// the local clock and a finished one with the server's FinishTime, so the finished snapshot can carry
/// an earlier timestamp than the in-progress one before it. The build then ignored the completion and
/// stayed Running until a newer run started (ktsu-dev/BuildMonitor#310).
/// </summary>
[TestClass]
public sealed class BuildStatusTransitionTests
{
	private static readonly DateTimeOffset Started = new(2026, 9, 28, 6, 0, 0, TimeSpan.Zero);

	private static Run RunOf(Build build, RunStatus status, DateTimeOffset lastUpdated)
	{
		Run run = build.CreateRun("1".As<RunName>());
		run.Started = Started;
		run.Status = status;
		run.LastUpdated = lastUpdated;
		return run;
	}

	[TestMethod]
	public void ACompletionStampedBeforeTheLastInProgressPollIsTaken()
	{
		// Arrange
		Build build = new();
		DateTimeOffset localPoll = Started.AddMinutes(5);
		Run run = RunOf(build, RunStatus.Running, localPoll);
		build.Runs[run.Id] = run;
		build.UpdateFromRun(run);

		// Act
		run.Status = RunStatus.Success;
		run.LastUpdated = localPoll.AddSeconds(-1);
		build.UpdateFromRun(run);

		// Assert
		Assert.AreEqual(RunStatus.Success, build.LastStatus);
		Assert.IsFalse(build.IsOngoing, "A finished run should not leave its build ongoing");
		Assert.AreEqual(localPoll.AddSeconds(-1), build.LastUpdated, "The build should take the run's finish time");
	}

	[TestMethod]
	public void AnOlderSnapshotOfAFinishedRunDoesNotReplaceANewerOne()
	{
		// Arrange
		Build build = new();
		Run run = RunOf(build, RunStatus.Failure, Started.AddMinutes(5));
		build.Runs[run.Id] = run;
		build.UpdateFromRun(run);

		// Act
		Run stale = RunOf(build, RunStatus.Success, Started.AddMinutes(4));
		build.UpdateFromRun(stale);

		// Assert
		Assert.AreEqual(RunStatus.Failure, build.LastStatus);
		Assert.AreEqual(Started.AddMinutes(5), build.LastUpdated);
	}

	[TestMethod]
	public void ANewerRunStillReplacesTheLatestOne()
	{
		// Arrange
		Build build = new();
		Run first = RunOf(build, RunStatus.Success, Started.AddMinutes(5));
		build.Runs[first.Id] = first;
		build.UpdateFromRun(first);

		// Act
		Run second = build.CreateRun("2".As<RunName>());
		second.Started = Started.AddMinutes(10);
		second.LastUpdated = Started.AddMinutes(11);
		second.Status = RunStatus.Running;
		build.UpdateFromRun(second);

		// Assert
		Assert.AreEqual(RunStatus.Running, build.LastStatus);
		Assert.AreEqual(second.Started, build.LastStarted);
	}
}
