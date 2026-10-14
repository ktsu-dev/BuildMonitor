// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests for <see cref="BuildMonitor.GetStatusColor"/>.
/// </summary>
[TestClass]
public sealed class StatusColorTests
{
	[TestMethod]
	public void ACanceledRunIsNotColouredLikeAFailure()
	{
		// Act
		System.Numerics.Vector4 canceled = BuildMonitor.GetStatusColor(RunStatus.Canceled).Value;
		System.Numerics.Vector4 failure = BuildMonitor.GetStatusColor(RunStatus.Failure).Value;

		// Assert
		Assert.AreNotEqual(failure, canceled);
	}

	[TestMethod]
	public void ACanceledRunIsNotColouredLikeAPendingRun()
	{
		// Act
		System.Numerics.Vector4 canceled = BuildMonitor.GetStatusColor(RunStatus.Canceled).Value;
		System.Numerics.Vector4 pending = BuildMonitor.GetStatusColor(RunStatus.Pending).Value;

		// Assert
		Assert.AreNotEqual(pending, canceled);
	}
}
