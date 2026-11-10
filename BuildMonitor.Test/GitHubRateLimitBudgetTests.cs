// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Octokit;

/// <summary>
/// Covers how a response's rate limit headers update the GitHub provider's budget. A job-log download
/// ends on a redirect to blob storage, whose response has no <c>X-RateLimit-*</c> headers, and
/// Octokit reports that as a limit of 0. Recording it read the budget as 0/0, which counts as
/// unknown, so the low-budget limits on polling switched off (ktsu-dev/BuildMonitor#401).
/// </summary>
[TestClass]
public sealed class GitHubRateLimitBudgetTests
{
	private static readonly DateTimeOffset Reset = new(2026, 11, 10, 7, 0, 0, TimeSpan.Zero);

	private static ApiInfo Info(int limit, int remaining, long resetEpochSeconds) =>
		new(new Dictionary<string, Uri>(), [], [], string.Empty, new RateLimit(limit, remaining, resetEpochSeconds));

	[TestMethod]
	public void AResponseWithARateLimitRecordsTheBudget()
	{
		// Arrange
		GitHub provider = new();

		// Act
		provider.UpdateRateLimitFromApiInfo(Info(5000, 600, Reset.ToUnixTimeSeconds()));

		// Assert
		Assert.AreEqual("600/5000", provider.RateLimitDisplay);
		Assert.IsTrue(provider.IsLowBudget);
	}

	[TestMethod]
	public void AResponseWithNoRateLimitHeadersKeepsTheRecordedBudget()
	{
		// Arrange
		GitHub provider = new();
		provider.UpdateRateLimitFromApiInfo(Info(5000, 600, Reset.ToUnixTimeSeconds()));

		// Act: what Octokit reports after following a job-log redirect to blob storage
		provider.UpdateRateLimitFromApiInfo(Info(0, 0, 0));

		// Assert
		Assert.AreEqual("600/5000", provider.RateLimitDisplay, "a response without rate limit headers says nothing about the budget");
		Assert.AreEqual(0.12, provider.BudgetPercentage, 1e-9);
		Assert.IsTrue(provider.IsLowBudget, "the low-budget limits should stay on after a failed run's logs are fetched");
	}

	[TestMethod]
	public void NoApiInfoKeepsTheRecordedBudget()
	{
		// Arrange
		GitHub provider = new();
		provider.UpdateRateLimitFromApiInfo(Info(5000, 600, Reset.ToUnixTimeSeconds()));

		// Act
		provider.UpdateRateLimitFromApiInfo(null);

		// Assert
		Assert.AreEqual("600/5000", provider.RateLimitDisplay);
	}
}
