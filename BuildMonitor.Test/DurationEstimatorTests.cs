// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using ktsu.Semantics.Strings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests for <see cref="DurationEstimator"/>.
/// </summary>
/// <remarks>
/// The estimator drives the Estimate and ETA columns and the adaptive polling interval in
/// <c>RunSync</c>, so a mistake here shows up as wrong numbers on screen and as the wrong request
/// rate against a rate-limited API. It is pure numeric logic over a build's run history, which
/// makes it the cheapest high-value thing in this repository to test.
/// </remarks>
[TestClass]
public sealed class DurationEstimatorTests
{
	/// <summary>
	/// Builds a <see cref="Build"/> whose successful runs have the given durations, most recent
	/// first, all on the same branch.
	/// </summary>
	private static Build BuildWithDurations(params double[] minutes) =>
		BuildWithDurations("main", minutes);

	private static Build BuildWithDurations(string branch, params double[] minutes)
	{
		Build build = new();
		DateTimeOffset start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

		for (int i = 0; i < minutes.Length; i++)
		{
			// Most recent first: earlier entries get later start times.
			DateTimeOffset started = start.AddHours(minutes.Length - i);
			Run run = new()
			{
				Id = $"run-{i}".As<RunId>(),
				Name = $"run-{i}".As<RunName>(),
				Status = RunStatus.Success,
				Started = started,
				LastUpdated = started.AddMinutes(minutes[i]),
				Branch = branch.As<BranchName>(),
			};

			_ = build.Runs.TryAdd(run.Id, run);
		}

		return build;
	}

	private static void AddRun(Build build, string id, RunStatus status, double minutes, string branch = "main")
	{
		DateTimeOffset started = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
		Run run = new()
		{
			Id = id.As<RunId>(),
			Name = id.As<RunName>(),
			Status = status,
			Started = started,
			LastUpdated = started.AddMinutes(minutes),
			Branch = branch.As<BranchName>(),
		};

		_ = build.Runs.TryAdd(run.Id, run);
	}

	/// <summary>
	/// A build with no runs cannot be estimated, and must report zero rather than guessing.
	/// </summary>
	[TestMethod]
	public void ABuildWithNoRunsEstimatesZero()
	{
		// Arrange
		Build build = new();

		// Act & Assert
		Assert.AreEqual(TimeSpan.Zero, DurationEstimator.EstimateDuration(build));
	}

	/// <summary>
	/// Fewer than the minimum sample count must report zero rather than estimating from one or two
	/// data points.
	/// </summary>
	/// <param name="sampleCount">How many successful runs the build has.</param>
	[TestMethod]
	[DataRow(1)]
	[DataRow(2)]
	public void FewerThanThreeSamplesEstimatesZero(int sampleCount)
	{
		// Arrange
		Build build = BuildWithDurations([.. Enumerable.Repeat(10.0, sampleCount)]);

		// Act & Assert
		Assert.AreEqual(TimeSpan.Zero, DurationEstimator.EstimateDuration(build));
	}

	/// <summary>
	/// Three identical samples must estimate exactly that duration, whatever weighting is applied.
	/// </summary>
	[TestMethod]
	public void IdenticalSamplesEstimateThatExactDuration()
	{
		// Arrange
		Build build = BuildWithDurations(10, 10, 10, 10, 10);

		// Act
		TimeSpan estimate = DurationEstimator.EstimateDuration(build);

		// Assert
		Assert.AreEqual(TimeSpan.FromMinutes(10), estimate);
	}

	/// <summary>
	/// A single wild outlier must not drag the estimate toward it. This is the whole point of the
	/// IQR filter.
	/// </summary>
	[TestMethod]
	public void ASingleWildOutlierDoesNotDragTheEstimate()
	{
		// Arrange -- seven runs around ten minutes, and one that took five hours
		Build build = BuildWithDurations(10, 11, 10, 9, 300, 10, 11, 10);

		// Act
		TimeSpan estimate = DurationEstimator.EstimateDuration(build);

		// Assert
		Assert.IsLessThan(TimeSpan.FromMinutes(20), estimate, "The 300-minute outlier should have been filtered out.");
		Assert.IsGreaterThan(TimeSpan.FromMinutes(5), estimate);
	}

	/// <summary>
	/// Recent runs must weigh more than older ones, so a build that has genuinely got slower is
	/// estimated closer to its recent times than to its historical ones.
	/// </summary>
	[TestMethod]
	public void RecentRunsWeighMoreThanOlderOnes()
	{
		// Arrange -- most recent first: the build recently doubled in duration
		Build build = BuildWithDurations(20, 20, 20, 10, 10, 10);

		// Act
		TimeSpan estimate = DurationEstimator.EstimateDuration(build);

		// Assert -- an unweighted mean would be 15 minutes
		Assert.IsGreaterThan(TimeSpan.FromMinutes(15), estimate,
			"Exponential weighting should pull the estimate toward the recent, slower runs.");
	}

	/// <summary>
	/// Failed, canceled and pending runs carry no useful duration and must not be sampled.
	/// </summary>
	[TestMethod]
	public void OnlySuccessfulRunsAreSampled()
	{
		// Arrange -- three successes at 10 minutes, plus noise at wildly different durations
		Build build = BuildWithDurations(10, 10, 10);
		AddRun(build, "failed", RunStatus.Failure, 120);
		AddRun(build, "canceled", RunStatus.Canceled, 240);
		AddRun(build, "pending", RunStatus.Pending, 480);

		// Act
		TimeSpan estimate = DurationEstimator.EstimateDuration(build);

		// Assert
		Assert.AreEqual(TimeSpan.FromMinutes(10), estimate);
	}

	/// <summary>
	/// A build whose only runs are unsuccessful has nothing to estimate from.
	/// </summary>
	[TestMethod]
	public void ABuildWithNoSuccessfulRunsEstimatesZero()
	{
		// Arrange
		Build build = new();
		AddRun(build, "a", RunStatus.Failure, 10);
		AddRun(build, "b", RunStatus.Failure, 11);
		AddRun(build, "c", RunStatus.Canceled, 12);

		// Act & Assert
		Assert.AreEqual(TimeSpan.Zero, DurationEstimator.EstimateDuration(build));
	}

	/// <summary>
	/// When a branch has enough history of its own, its estimate must be used rather than the
	/// build-wide one -- that is the point of the branch-specific overload.
	/// </summary>
	[TestMethod]
	public void ABranchWithEnoughHistoryUsesItsOwnEstimate()
	{
		// Arrange -- main is fast, release is slow
		Build build = BuildWithDurations("main", 5, 5, 5, 5);
		AddRun(build, "rel-1", RunStatus.Success, 30, branch: "release");
		AddRun(build, "rel-2", RunStatus.Success, 30, branch: "release");
		AddRun(build, "rel-3", RunStatus.Success, 30, branch: "release");

		// Act
		TimeSpan releaseEstimate = DurationEstimator.EstimateDuration(build, "release".As<BranchName>());

		// Assert
		Assert.AreEqual(TimeSpan.FromMinutes(30), releaseEstimate);
	}

	/// <summary>
	/// A branch with too little history of its own must fall back to the build-wide estimate
	/// rather than reporting zero.
	/// </summary>
	[TestMethod]
	public void ABranchWithTooLittleHistoryFallsBackToTheBuildEstimate()
	{
		// Arrange -- four runs on main, a single one on a feature branch
		Build build = BuildWithDurations("main", 10, 10, 10, 10);
		AddRun(build, "feat-1", RunStatus.Success, 45, branch: "feature");

		// Act
		TimeSpan featureEstimate = DurationEstimator.EstimateDuration(build, "feature".As<BranchName>());

		// Assert -- not zero, and not the lone 45-minute sample
		Assert.AreNotEqual(TimeSpan.Zero, featureEstimate);
		Assert.IsLessThan(TimeSpan.FromMinutes(45), featureEstimate);
	}

	/// <summary>
	/// A branch that has never run must fall back rather than failing.
	/// </summary>
	[TestMethod]
	public void AnUnknownBranchFallsBackToTheBuildEstimate()
	{
		// Arrange
		Build build = BuildWithDurations("main", 10, 10, 10, 10);

		// Act
		TimeSpan estimate = DurationEstimator.EstimateDuration(build, "never-built".As<BranchName>());

		// Assert
		Assert.AreEqual(TimeSpan.FromMinutes(10), estimate);
	}

	/// <summary>
	/// Estimation must be deterministic: the same history must always produce the same number, or
	/// the Estimate column would flicker between frames.
	/// </summary>
	[TestMethod]
	public void EstimationIsDeterministic()
	{
		// Arrange
		Build build = BuildWithDurations(12, 9, 14, 11, 40, 10, 13);

		// Act
		TimeSpan first = DurationEstimator.EstimateDuration(build);
		TimeSpan second = DurationEstimator.EstimateDuration(build);
		TimeSpan third = DurationEstimator.EstimateDuration(build);

		// Assert
		Assert.AreEqual(first, second);
		Assert.AreEqual(second, third);
	}

	/// <summary>
	/// The estimate must stay inside the range of the samples it was drawn from -- a weighted
	/// average that escaped its own inputs would be a bug.
	/// </summary>
	[TestMethod]
	public void TheEstimateStaysWithinTheSampleRange()
	{
		// Arrange
		double[] durations = [8, 12, 10, 14, 9, 11, 13];
		Build build = BuildWithDurations(durations);

		// Act
		TimeSpan estimate = DurationEstimator.EstimateDuration(build);

		// Assert
		Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMinutes(durations.Min()), estimate);
		Assert.IsLessThanOrEqualTo(TimeSpan.FromMinutes(durations.Max()), estimate);
	}

	/// <summary>
	/// An ongoing run has no final duration and must never be sampled, or the estimate would be
	/// dragged toward however long the run happens to have been going.
	/// </summary>
	[TestMethod]
	public void OngoingRunsAreNotSampled()
	{
		// Arrange
		Build build = BuildWithDurations(10, 10, 10);
		AddRun(build, "running", RunStatus.Running, 999);

		// Act
		TimeSpan estimate = DurationEstimator.EstimateDuration(build);

		// Assert
		Assert.AreEqual(TimeSpan.FromMinutes(10), estimate);
	}
}
