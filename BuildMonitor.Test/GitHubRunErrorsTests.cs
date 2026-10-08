// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using System.Collections.Generic;
using System.Net;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Octokit;

/// <summary>
/// Tests the error text a failed GitHub run ends up with, and when its jobs are fetched for it.
/// </summary>
/// <remarks>
/// Only jobs that concluded <c>failure</c> were read for errors. A run that failed at startup has no
/// jobs, and a timed-out job concludes <c>timed_out</c>, so such runs showed as failed with an empty
/// Errors column, and because the column was empty their jobs were fetched again on every update
/// (ktsu-dev/BuildMonitor#311).
/// </remarks>
[TestClass]
public sealed class GitHubRunErrorsTests
{
	[TestMethod]
	public void AStartupFailureWithNoJobsGetsARunLevelError()
	{
		// Arrange
		StringEnum<WorkflowRunConclusion> conclusion = new(WorkflowRunConclusion.StartupFailure);

		// Act
		IReadOnlyList<string> errors = GitHub.WithRunLevelFallback([], conclusion);

		// Assert
		Assert.HasCount(1, errors);
		Assert.AreEqual("Workflow failed: startup_failure", errors[0]);
	}

	[TestMethod]
	public void JobErrorsAreKeptWithoutARunLevelMessage()
	{
		// Arrange
		List<string> jobErrors = ["[build] error CS1002: ; expected"];

		// Act
		IReadOnlyList<string> errors = GitHub.WithRunLevelFallback(jobErrors, new StringEnum<WorkflowRunConclusion>(WorkflowRunConclusion.Failure));

		// Assert
		CollectionAssert.AreEqual(jobErrors, (System.Collections.ICollection)errors);
	}

	[TestMethod]
	public void ASecondUpdateOfAFailedRunDoesNotFetchItsJobsAgain()
	{
		// Arrange: the first update of a startup failure fetches its jobs and finds none
		Assert.IsTrue(GitHub.ShouldFetchRunErrors(RunStatus.Failure, RunStatus.Pending, []));
		IReadOnlyList<string> errors = GitHub.WithRunLevelFallback([], new StringEnum<WorkflowRunConclusion>(WorkflowRunConclusion.StartupFailure));

		// Act
		bool fetchAgain = GitHub.ShouldFetchRunErrors(RunStatus.Failure, RunStatus.Failure, errors);

		// Assert
		Assert.IsFalse(fetchAgain);
	}

	[TestMethod]
	[DataRow(WorkflowJobConclusion.Failure, true)]
	[DataRow(WorkflowJobConclusion.TimedOut, true)]
	[DataRow(WorkflowJobConclusion.Cancelled, false)]
	[DataRow(WorkflowJobConclusion.Success, false)]
	public void TimedOutJobsAreReadForErrorsLikeFailedOnes(WorkflowJobConclusion conclusion, bool expected) =>
		Assert.AreEqual(expected, GitHub.IsFailedJob(new StringEnum<WorkflowJobConclusion>(conclusion)));

	[TestMethod]
	public void ATimedOutJobSaysSo() =>
		Assert.AreEqual("[test] Timed out", GitHub.DescribeFailedJob("test", new StringEnum<WorkflowJobConclusion>(WorkflowJobConclusion.TimedOut)));

	/// <summary>
	/// A response built to order, as in <see cref="GitHubRequestOutcomeTests"/>: the provider reads
	/// only the status code and the headers off it.
	/// </summary>
	private sealed class FakeResponse(HttpStatusCode statusCode, IReadOnlyDictionary<string, string> headers) : IResponse
	{
		public object Body => "{}";
		public IReadOnlyDictionary<string, string> Headers { get; } = headers;
		public ApiInfo ApiInfo => null!;
		public HttpStatusCode StatusCode { get; } = statusCode;
		public string ContentType => "application/json";
	}

	private static ApiException ApiFailure(HttpStatusCode statusCode, IReadOnlyDictionary<string, string>? headers = null) =>
		new(new FakeResponse(statusCode, headers ?? new Dictionary<string, string>()));

	/// <summary>
	/// The error fetch is best effort and swallows API failures, but a rate limit has to reach the
	/// request wrapper so that its backoff applies.
	/// </summary>
	[TestMethod]
	public void RateLimitsAreToldApartFromOtherApiFailures()
	{
		Assert.IsTrue(GitHub.IsRateLimitException(ApiFailure(HttpStatusCode.TooManyRequests)));
		Assert.IsTrue(GitHub.IsRateLimitException(ApiFailure(HttpStatusCode.Forbidden, new Dictionary<string, string> { ["X-RateLimit-Remaining"] = "0" })));
		Assert.IsFalse(GitHub.IsRateLimitException(ApiFailure(HttpStatusCode.Forbidden)));
		Assert.IsFalse(GitHub.IsRateLimitException(ApiFailure(HttpStatusCode.InternalServerError)));
	}
}
