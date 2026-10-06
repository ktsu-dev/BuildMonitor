// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using System;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Octokit;

/// <summary>
/// Tests how a GitHub workflow run's status and conclusion map onto a <see cref="RunStatus"/>.
/// </summary>
/// <remarks>
/// A run that is still ongoing is polled every 10-60 s, so anything the mapping logs is logged on
/// every poll. A run waiting for deployment environment approval used to add an "Unhandled workflow
/// status" warning each time, and after a few hours of one pending approval the 1000-entry log held
/// nothing else (ktsu-dev/BuildMonitor#306).
/// </remarks>
[TestClass]
public sealed class GitHubWorkflowStatusTests
{
	/// <summary>
	/// Waiting and Pending are known, normal states: they map to Pending and log nothing.
	/// </summary>
	/// <param name="runStatus">The workflow run status under test.</param>
	[TestMethod]
	[DataRow(WorkflowRunStatus.Waiting)]
	[DataRow(WorkflowRunStatus.Pending)]
	public void KnownWaitingStatusesMapToPendingWithoutAWarning(WorkflowRunStatus runStatus)
	{
		// Arrange
		StringEnum<WorkflowRunStatus> status = new(runStatus);

		// Act
		RunStatus first = GitHub.MapWorkflowRunStatus(status, null);
		RunStatus second = GitHub.MapWorkflowRunStatus(status, null);

		// Assert
		Assert.AreEqual(RunStatus.Pending, first);
		Assert.AreEqual(RunStatus.Pending, second);
		Assert.IsFalse(
			Log.GetEntries().Any(e => e.Message.Contains($"Unhandled workflow status: {status}", StringComparison.Ordinal)),
			"A known status should not be reported as unhandled");
	}

	/// <summary>
	/// A status this provider does not know is still treated as Pending, and is reported once rather
	/// than once per poll.
	/// </summary>
	[TestMethod]
	public void UnknownStatusIsTreatedAsPendingAndReportedOnce()
	{
		// Arrange
		string unknown = $"unknown_{Guid.NewGuid():N}";
		StringEnum<WorkflowRunStatus> status = new(unknown);

		// Act
		RunStatus first = GitHub.MapWorkflowRunStatus(status, null);
		RunStatus second = GitHub.MapWorkflowRunStatus(status, null);

		// Assert
		Assert.AreEqual(RunStatus.Pending, first);
		Assert.AreEqual(RunStatus.Pending, second);
		Assert.AreEqual(1, Log.GetEntries().Count(e => e.Message.Contains(unknown, StringComparison.Ordinal)));
	}

	/// <summary>
	/// The explicit arms are unchanged: in progress is Running and a completed success is Success.
	/// </summary>
	[TestMethod]
	public void KnownStatusesStillMapAsBefore()
	{
		Assert.AreEqual(RunStatus.Pending, GitHub.MapWorkflowRunStatus(WorkflowRunStatus.Queued, null));
		Assert.AreEqual(RunStatus.Running, GitHub.MapWorkflowRunStatus(WorkflowRunStatus.InProgress, null));
		Assert.AreEqual(RunStatus.Success, GitHub.MapWorkflowRunStatus(WorkflowRunStatus.Completed, WorkflowRunConclusion.Success));
		Assert.AreEqual(RunStatus.Failure, GitHub.MapWorkflowRunStatus(WorkflowRunStatus.Completed, WorkflowRunConclusion.Failure));
	}
}
