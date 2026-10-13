// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests for <see cref="BuildMonitor.SelectHistoryRuns"/>.
/// </summary>
/// <remarks>
/// The History column draws its indicators left to right, and a column narrower than all of them
/// clips on the right. The newest runs therefore have to come first, so clipping drops the oldest.
/// </remarks>
[TestClass]
public sealed class HistoryRunsTests
{
	private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// Builds runs with the given statuses, most recent first, an hour apart.
	/// </summary>
	private static List<Run> RunsNewestFirst(params RunStatus[] statuses) =>
		[.. statuses.Select((status, i) => new Run { Status = status, Started = Start.AddHours(-i) })];

	[TestMethod]
	public void TheNewestRunIsDrawnFirst()
	{
		// Arrange
		List<Run> branchRuns = RunsNewestFirst(RunStatus.Running, RunStatus.Success, RunStatus.Success, RunStatus.Success, RunStatus.Success);

		// Act
		List<Run> history = BuildMonitor.SelectHistoryRuns(branchRuns);

		// Assert
		CollectionAssert.AreEqual(branchRuns, history);
	}

	[TestMethod]
	public void OnlyTheFiveNewestRunsAreShown()
	{
		// Arrange
		List<Run> branchRuns = RunsNewestFirst(RunStatus.Running, RunStatus.Failure, RunStatus.Success, RunStatus.Success, RunStatus.Success, RunStatus.Failure);

		// Act
		List<Run> history = BuildMonitor.SelectHistoryRuns(branchRuns);

		// Assert
		CollectionAssert.AreEqual(branchRuns.Take(5).ToList(), history);
	}

	[TestMethod]
	public void CanceledRunsAreSkipped()
	{
		// Arrange
		List<Run> branchRuns = RunsNewestFirst(RunStatus.Canceled, RunStatus.Success, RunStatus.Failure);

		// Act
		List<Run> history = BuildMonitor.SelectHistoryRuns(branchRuns);

		// Assert
		CollectionAssert.AreEqual(branchRuns.Skip(1).ToList(), history);
	}
}
