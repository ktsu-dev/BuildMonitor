// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Octokit;

/// <summary>
/// Covers when a rate-limited GitHub request may be retried. Only <c>X-RateLimit-Reset</c> was read,
/// which on a secondary limit is the end of the primary hourly window, so a limit that asked for a
/// minute paused polling for the 10-minute cap (ktsu-dev/BuildMonitor#385).
/// </summary>
[TestClass]
public sealed class GitHubRateLimitResetTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

	private sealed class FakeResponse(HttpStatusCode statusCode, IReadOnlyDictionary<string, string> headers) : IResponse
	{
		public object Body => "{}";
		public IReadOnlyDictionary<string, string> Headers { get; } = headers;
		public ApiInfo ApiInfo => null!;
		public HttpStatusCode StatusCode { get; } = statusCode;
		public string ContentType => "application/json";
	}

	private static ApiException ApiFailure(HttpStatusCode statusCode, Dictionary<string, string> headers) =>
		new(new FakeResponse(statusCode, headers));

	private static string UnixSeconds(DateTimeOffset time) =>
		time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

	/// <summary>
	/// The case from the issue: a secondary limit with most of the hourly budget left.
	/// </summary>
	[TestMethod]
	public void ASecondaryLimitWaitsForItsRetryAfterRatherThanThePrimaryReset()
	{
		ApiException secondary = ApiFailure(HttpStatusCode.Forbidden, new()
		{
			["X-RateLimit-Remaining"] = "4000",
			["X-RateLimit-Reset"] = UnixSeconds(Now.AddMinutes(45)),
			["Retry-After"] = "60",
		});

		Assert.AreEqual(Now.AddSeconds(60), GitHub.ParseRateLimitResetTime(secondary, Now));
	}

	[TestMethod]
	public void ASpentPrimaryBudgetWaitsForTheReset()
	{
		DateTimeOffset reset = Now.AddMinutes(45);
		ApiException primary = ApiFailure(HttpStatusCode.Forbidden, new()
		{
			["X-RateLimit-Remaining"] = "0",
			["X-RateLimit-Reset"] = UnixSeconds(reset),
		});

		Assert.AreEqual(reset, GitHub.ParseRateLimitResetTime(primary, Now));
	}

	/// <summary>
	/// GitHub's guidance for a secondary limit that names no wait is to back off for at least a
	/// minute, not until the primary window resets.
	/// </summary>
	[TestMethod]
	public void ASecondaryLimitWithNoRetryAfterBacksOffForAMinute()
	{
		ApiException secondary = ApiFailure(HttpStatusCode.Forbidden, new()
		{
			["X-RateLimit-Remaining"] = "4000",
			["X-RateLimit-Reset"] = UnixSeconds(Now.AddMinutes(45)),
		});

		Assert.AreEqual(Now + GitHub.SecondaryRateLimitBackoff, GitHub.ParseRateLimitResetTime(secondary, Now));
	}

	[TestMethod]
	public void ARetryAfterGivenAsAnHttpDateIsHonoured()
	{
		DateTimeOffset retryAt = Now.AddSeconds(90);
		ApiException limited = ApiFailure(HttpStatusCode.TooManyRequests, new()
		{
			["Retry-After"] = retryAt.ToString("r", CultureInfo.InvariantCulture),
		});

		Assert.AreEqual(retryAt, GitHub.ParseRateLimitResetTime(limited, Now));
	}

	/// <summary>
	/// GitHub sends the rate-limit headers in lowercase and proxies re-case them, but Octokit keys
	/// them case-sensitively as they arrived, so an exact-case lookup missed a spent budget and
	/// retried it every minute (ktsu-dev/BuildMonitor#389).
	/// </summary>
	[TestMethod]
	[DataRow("x-ratelimit-remaining", "x-ratelimit-reset")]
	[DataRow("X-Ratelimit-Remaining", "X-Ratelimit-Reset")]
	public void ASpentPrimaryBudgetWaitsForTheResetWhateverTheHeaderCasing(string remainingName, string resetName)
	{
		DateTimeOffset reset = Now.AddMinutes(45);
		ApiException primary = ApiFailure(HttpStatusCode.Forbidden, new()
		{
			[remainingName] = "0",
			["x-ratelimit-limit"] = "5000",
			[resetName] = UnixSeconds(reset),
		});

		Assert.AreEqual(reset, GitHub.ParseRateLimitResetTime(primary, Now));
	}

	[TestMethod]
	public void ALowercaseRetryAfterIsHonoured()
	{
		ApiException limited = ApiFailure(HttpStatusCode.TooManyRequests, new()
		{
			["retry-after"] = "30",
		});

		Assert.AreEqual(Now.AddSeconds(30), GitHub.ParseRateLimitResetTime(limited, Now));
	}

	/// <summary>
	/// A 403 whose message does not mention a rate limit is told apart by its spent budget alone.
	/// </summary>
	[TestMethod]
	[DataRow("x-ratelimit-remaining")]
	[DataRow("X-Ratelimit-Remaining")]
	public void AForbiddenResponseWithASpentBudgetIsARateLimitWhateverTheHeaderCasing(string remainingName)
	{
		ApiException forbidden = ApiFailure(HttpStatusCode.Forbidden, new()
		{
			[remainingName] = "0",
		});

		Assert.DoesNotContain("rate limit", forbidden.Message, StringComparison.OrdinalIgnoreCase);
		Assert.IsTrue(GitHub.IsRateLimitException(forbidden));
	}
}
