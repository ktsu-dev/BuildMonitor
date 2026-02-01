// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

/// <summary>
/// Provides sophisticated build duration estimation using statistical methods.
/// </summary>
internal static class DurationEstimator
{
	/// <summary>
	/// Minimum number of samples required for a reliable estimate.
	/// </summary>
	private const int MinSamplesForEstimate = 3;

	/// <summary>
	/// Maximum number of recent runs to consider for estimation.
	/// </summary>
	private const int MaxSamplesToConsider = 20;

	/// <summary>
	/// Decay factor for exponential weighting (0-1). Higher values give more weight to recent runs.
	/// A value of 0.3 means each older run has 70% the weight of the next newer run.
	/// </summary>
	private const double ExponentialDecayFactor = 0.3;

	/// <summary>
	/// IQR multiplier for outlier detection. Values outside Q1 - k*IQR or Q3 + k*IQR are outliers.
	/// Using 1.5 as the standard Tukey fence.
	/// </summary>
	private const double IqrMultiplier = 1.5;

	/// <summary>
	/// Calculates the estimated duration for a build on a specific branch.
	/// Uses branch-specific data when available, falls back to overall build data.
	/// </summary>
	/// <param name="build">The build to estimate duration for.</param>
	/// <param name="branch">The specific branch to estimate for, or null for overall estimate.</param>
	/// <returns>The estimated duration, or TimeSpan.Zero if insufficient data.</returns>
	public static TimeSpan EstimateDuration(Build build, BranchName? branch = null)
	{
		// Get completed successful runs, excluding any ongoing runs
		List<Run> allSuccessfulRuns = [.. build.Runs.Values
			.Where(r => r.Status == RunStatus.Success && !r.IsOngoing)
			.OrderByDescending(r => r.Started)
			.Take(MaxSamplesToConsider)];

		if (allSuccessfulRuns.Count == 0)
		{
			return TimeSpan.Zero;
		}

		// Try branch-specific estimation first
		if (branch is not null)
		{
			List<Run> branchRuns = [.. allSuccessfulRuns.Where(r => r.Branch == branch)];
			TimeSpan branchEstimate = CalculateEstimate(branchRuns);

			if (branchEstimate > TimeSpan.Zero)
			{
				return branchEstimate;
			}
		}

		// Fall back to overall build estimation
		return CalculateEstimate(allSuccessfulRuns);
	}

	/// <summary>
	/// Calculates an estimated duration from a list of runs using statistical methods.
	/// </summary>
	/// <param name="runs">The runs to calculate estimation from (should be ordered by Started descending).</param>
	/// <returns>The estimated duration, or TimeSpan.Zero if insufficient data.</returns>
	private static TimeSpan CalculateEstimate(List<Run> runs)
	{
		if (runs.Count < MinSamplesForEstimate)
		{
			return TimeSpan.Zero;
		}

		// Extract durations in seconds
		List<double> durations = [.. runs.Select(r => r.Duration.TotalSeconds)];

		// Remove outliers using IQR method
		List<double> filteredDurations = RemoveOutliers(durations);

		// Need minimum samples after filtering
		if (filteredDurations.Count < MinSamplesForEstimate)
		{
			// If too many were filtered, use the original data with median
			return TimeSpan.FromSeconds(CalculateMedian(durations));
		}

		// Calculate exponentially weighted average
		double weightedAverage = CalculateExponentiallyWeightedAverage(filteredDurations);

		return TimeSpan.FromSeconds(weightedAverage);
	}

	/// <summary>
	/// Removes statistical outliers using the IQR (Interquartile Range) method.
	/// </summary>
	/// <param name="values">The values to filter.</param>
	/// <returns>Values with outliers removed.</returns>
	private static List<double> RemoveOutliers(List<double> values)
	{
		if (values.Count < 4)
		{
			// Not enough data for meaningful IQR calculation
			return values;
		}

		List<double> sorted = [.. values.OrderBy(v => v)];

		double q1 = CalculatePercentile(sorted, 25);
		double q3 = CalculatePercentile(sorted, 75);
		double iqr = q3 - q1;

		double lowerBound = q1 - (IqrMultiplier * iqr);
		double upperBound = q3 + (IqrMultiplier * iqr);

		return [.. values.Where(v => v >= lowerBound && v <= upperBound)];
	}

	/// <summary>
	/// Calculates a percentile value from a sorted list.
	/// </summary>
	/// <param name="sortedValues">The sorted values.</param>
	/// <param name="percentile">The percentile to calculate (0-100).</param>
	/// <returns>The percentile value.</returns>
	private static double CalculatePercentile(List<double> sortedValues, double percentile)
	{
		if (sortedValues.Count == 0)
		{
			return 0;
		}

		if (sortedValues.Count == 1)
		{
			return sortedValues[0];
		}

		double index = percentile / 100.0 * (sortedValues.Count - 1);
		int lower = (int)Math.Floor(index);
		int upper = (int)Math.Ceiling(index);

		if (lower == upper)
		{
			return sortedValues[lower];
		}

		// Linear interpolation
		double fraction = index - lower;
		return sortedValues[lower] + (fraction * (sortedValues[upper] - sortedValues[lower]));
	}

	/// <summary>
	/// Calculates the median of a list of values.
	/// </summary>
	/// <param name="values">The values to calculate median for.</param>
	/// <returns>The median value.</returns>
	private static double CalculateMedian(List<double> values) =>
		CalculatePercentile([.. values.OrderBy(v => v)], 50);

	/// <summary>
	/// Calculates an exponentially weighted average where recent values have more weight.
	/// The first value (most recent) has the highest weight.
	/// </summary>
	/// <param name="values">Values ordered from most recent to oldest.</param>
	/// <returns>The exponentially weighted average.</returns>
	private static double CalculateExponentiallyWeightedAverage(List<double> values)
	{
		if (values.Count == 0)
		{
			return 0;
		}

		if (values.Count == 1)
		{
			return values[0];
		}

		double weightSum = 0;
		double weightedSum = 0;

		for (int i = 0; i < values.Count; i++)
		{
			// Weight decreases exponentially with age
			// Most recent (i=0) has weight 1.0
			// Each subsequent has weight * (1 - decayFactor)
			double weight = Math.Pow(1 - ExponentialDecayFactor, i);
			weightedSum += values[i] * weight;
			weightSum += weight;
		}

		return weightedSum / weightSum;
	}

	/// <summary>
	/// Gets estimation statistics for debugging/display purposes.
	/// </summary>
	/// <param name="build">The build to get stats for.</param>
	/// <param name="branch">Optional branch filter.</param>
	/// <returns>Statistics about the estimation.</returns>
	public static EstimationStats GetEstimationStats(Build build, BranchName? branch = null)
	{
		List<Run> allSuccessfulRuns = [.. build.Runs.Values
			.Where(r => r.Status == RunStatus.Success && !r.IsOngoing)
			.OrderByDescending(r => r.Started)
			.Take(MaxSamplesToConsider)];

		List<Run> runsUsed = branch is not null
			? [.. allSuccessfulRuns.Where(r => r.Branch == branch)]
			: allSuccessfulRuns;

		if (runsUsed.Count == 0)
		{
			return new EstimationStats(0, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, false);
		}

		List<double> durations = [.. runsUsed.Select(r => r.Duration.TotalSeconds)];
		List<double> filteredDurations = RemoveOutliers(durations);

		int sampleCount = filteredDurations.Count >= MinSamplesForEstimate ? filteredDurations.Count : durations.Count;
		bool usedFiltering = filteredDurations.Count >= MinSamplesForEstimate;

		List<double> finalDurations = usedFiltering ? filteredDurations : durations;

		double min = finalDurations.Min();
		double max = finalDurations.Max();
		double median = CalculateMedian(finalDurations);
		double estimate = usedFiltering
			? CalculateExponentiallyWeightedAverage(filteredDurations)
			: CalculateMedian(durations);

		return new EstimationStats(
			sampleCount,
			TimeSpan.FromSeconds(min),
			TimeSpan.FromSeconds(max),
			TimeSpan.FromSeconds(median),
			TimeSpan.FromSeconds(estimate),
			usedFiltering);
	}
}

/// <summary>
/// Statistics about a duration estimation for debugging/display.
/// </summary>
/// <param name="SampleCount">Number of samples used in the estimate.</param>
/// <param name="Min">Minimum duration in the sample set.</param>
/// <param name="Max">Maximum duration in the sample set.</param>
/// <param name="Median">Median duration in the sample set.</param>
/// <param name="Estimate">The calculated estimate.</param>
/// <param name="UsedOutlierFiltering">Whether outlier filtering was applied.</param>
internal sealed record EstimationStats(
	int SampleCount,
	TimeSpan Min,
	TimeSpan Max,
	TimeSpan Median,
	TimeSpan Estimate,
	bool UsedOutlierFiltering);
