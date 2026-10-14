// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests for <see cref="BuildMonitor.MeasureRun"/>.
/// </summary>
/// <remarks>
/// A pending run is queued rather than building, so the time since it was queued must not show up as
/// progress or shorten its ETA.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class RunProgressTests
{
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
	private static readonly TimeSpan Estimate = TimeSpan.FromMinutes(4);

	[TestInitialize]
	public void FreezeClock() => Clock.UtcNowOverride = () => Now;

	[TestCleanup]
	public void ReleaseClock() => Clock.UtcNowOverride = null;

	[TestMethod]
	public void APendingRunShowsNoProgressAndTheWholeEstimateAsItsEta()
	{
		// Arrange
		Run run = new() { Status = RunStatus.Pending, Started = Now.AddSeconds(-40) };

		// Act
		(TimeSpan duration, TimeSpan eta, double progress) = BuildMonitor.MeasureRun(run, Estimate);

		// Assert
		Assert.AreEqual(TimeSpan.Zero, duration);
		Assert.AreEqual(Estimate, eta);
		Assert.AreEqual(0, progress);
	}

	[TestMethod]
	public void ARunningRunCountsTheTimeSinceItStarted()
	{
		// Arrange
		Run run = new() { Status = RunStatus.Running, Started = Now.AddMinutes(-1) };

		// Act
		(TimeSpan duration, TimeSpan eta, double progress) = BuildMonitor.MeasureRun(run, Estimate);

		// Assert
		Assert.AreEqual(TimeSpan.FromMinutes(1), duration);
		Assert.AreEqual(TimeSpan.FromMinutes(3), eta);
		Assert.AreEqual(0.25, progress, 1e-9);
	}

	[TestMethod]
	public void AFinishedRunUsesItsRecordedDuration()
	{
		// Arrange
		Run run = new() { Status = RunStatus.Success, Started = Now.AddHours(-1), LastUpdated = Now.AddHours(-1).AddMinutes(2) };

		// Act
		(TimeSpan duration, TimeSpan eta, double progress) = BuildMonitor.MeasureRun(run, Estimate);

		// Assert
		Assert.AreEqual(TimeSpan.FromMinutes(2), duration);
		Assert.AreEqual(TimeSpan.FromMinutes(2), eta);
		Assert.AreEqual(0.5, progress, 1e-9);
	}
}
