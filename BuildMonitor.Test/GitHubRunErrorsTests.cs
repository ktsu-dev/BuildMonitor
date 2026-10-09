// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

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

	/// <summary>
	/// A jobs client that serves the jobs and logs it is given, counts its calls, and can be made to
	/// fail. Only the two calls the error fetch makes are implemented.
	/// </summary>
	private sealed class FakeJobsClient(IReadOnlyList<WorkflowJob> jobs) : IActionsWorkflowJobsClient
	{
		public Dictionary<long, string> Logs { get; } = [];
		public Exception? ListFailure { get; set; }
		public Exception? LogFailure { get; set; }
		public int ListCalls { get; private set; }

		public Task<WorkflowJobsResponse> List(string owner, string repo, long runId)
		{
			ListCalls++;
			return ListFailure is null
				? Task.FromResult(new WorkflowJobsResponse(jobs.Count, jobs))
				: Task.FromException<WorkflowJobsResponse>(ListFailure);
		}

		public Task<string> GetLogs(string owner, string repo, long jobId) =>
			LogFailure is not null ? Task.FromException<string>(LogFailure)
			: Logs.TryGetValue(jobId, out string? log) ? Task.FromResult(log)
			: Task.FromException<string>(new NotFoundException("No logs", HttpStatusCode.NotFound));

		public Task Rerun(string owner, string repo, long jobId) => throw new NotSupportedException();
		public Task<WorkflowJob> Get(string owner, string repo, long jobId) => throw new NotSupportedException();
		public Task<WorkflowJobsResponse> List(string owner, string repo, long runId, WorkflowRunJobsRequest workflowRunJobsRequest) => throw new NotSupportedException();
		public Task<WorkflowJobsResponse> List(string owner, string repo, long runId, WorkflowRunJobsRequest workflowRunJobsRequest, ApiOptions options) => throw new NotSupportedException();
		public Task<WorkflowJobsResponse> List(string owner, string repo, long runId, int attemptNumber) => throw new NotSupportedException();
		public Task<WorkflowJobsResponse> List(string owner, string repo, long runId, int attemptNumber, ApiOptions options) => throw new NotSupportedException();
	}

	private static WorkflowJob Job(long id, string name, WorkflowJobConclusion conclusion, params WorkflowJobStep[] steps) =>
		new(id, 1, "", "", "", "", "", WorkflowJobStatus.Completed, conclusion, null, DateTimeOffset.UnixEpoch, null, name, steps, "", [], null, "", null, "");

	private static WorkflowJobStep Step(string name, WorkflowJobConclusion conclusion) =>
		new(name, WorkflowJobStatus.Completed, conclusion, 1, null, null);

	private static Run FailedRun() => new()
	{
		Id = "42".As<RunId>(),
		Owner = new() { Name = "ktsu-dev".As<OwnerName>() },
		Repository = new() { Name = "BuildMonitor".As<RepositoryName>() },
		Status = RunStatus.Failure,
	};

	private static void AssertErrors(Run run, params string[] expected) =>
		CollectionAssert.AreEqual(expected, (System.Collections.ICollection)run.Errors);

	private static StringEnum<WorkflowRunConclusion> Concluded(WorkflowRunConclusion conclusion) => new(conclusion);

	/// <summary>
	/// The scenario from the issue: a workflow file with invalid YAML gives a run with no jobs.
	/// </summary>
	[TestMethod]
	public async Task FetchingAStartupFailureLeavesARunLevelErrorAndIsNotRepeated()
	{
		// Arrange
		FakeJobsClient jobs = new([]);
		Run run = FailedRun();

		// Act
		await GitHub.FetchRunErrorsAsync(jobs, run, Concluded(WorkflowRunConclusion.StartupFailure)).ConfigureAwait(false);

		// Assert
		AssertErrors(run, "Workflow failed: startup_failure");
		Assert.IsFalse(GitHub.ShouldFetchRunErrors(run.Status, RunStatus.Failure, run.Errors), "A second update should not fetch the jobs again");
		Assert.AreEqual(1, jobs.ListCalls);
	}

	[TestMethod]
	public async Task ATimedOutJobIsReportedFromItsLog()
	{
		// Arrange
		FakeJobsClient jobs = new([Job(7, "build", WorkflowJobConclusion.TimedOut)]);
		jobs.Logs[7] = "##[error]The job running on runner X has exceeded the maximum execution time of 10 minutes.";
		Run run = FailedRun();

		// Act
		await GitHub.FetchRunErrorsAsync(jobs, run, Concluded(WorkflowRunConclusion.TimedOut)).ConfigureAwait(false);

		// Assert
		Assert.HasCount(1, run.Errors);
		StringAssert.StartsWith(run.Errors[0], "[build] ");
		StringAssert.Contains(run.Errors[0], "exceeded the maximum execution time");
	}

	[TestMethod]
	public async Task AJobWithNoLogFallsBackToItsFailedStepsOrItsConclusion()
	{
		// Arrange
		FakeJobsClient jobs = new([
			Job(1, "test", WorkflowJobConclusion.Failure, Step("Checkout", WorkflowJobConclusion.Success), Step("Run tests", WorkflowJobConclusion.Failure)),
			Job(2, "deploy", WorkflowJobConclusion.TimedOut),
			Job(3, "lint", WorkflowJobConclusion.Failure),
			Job(4, "docs", WorkflowJobConclusion.Cancelled),
		]);
		Run run = FailedRun();

		// Act
		await GitHub.FetchRunErrorsAsync(jobs, run, Concluded(WorkflowRunConclusion.Failure)).ConfigureAwait(false);

		// Assert
		AssertErrors(run, "[test] Run tests", "[deploy] Timed out", "[lint] Failed");
	}

	[TestMethod]
	public async Task AFailedRunWhoseJobsWereAllCancelledStillSaysWhy()
	{
		// Arrange
		FakeJobsClient jobs = new([Job(1, "build", WorkflowJobConclusion.Cancelled)]);
		Run run = FailedRun();

		// Act
		await GitHub.FetchRunErrorsAsync(jobs, run, Concluded(WorkflowRunConclusion.Failure)).ConfigureAwait(false);

		// Assert
		AssertErrors(run, "Workflow failed: failure");
	}

	[TestMethod]
	public async Task JobsThatAreGoneRecordTheRunLevelError()
	{
		// Arrange
		FakeJobsClient jobs = new([]) { ListFailure = new NotFoundException("Gone", HttpStatusCode.NotFound) };
		Run run = FailedRun();

		// Act
		await GitHub.FetchRunErrorsAsync(jobs, run, Concluded(WorkflowRunConclusion.StartupFailure)).ConfigureAwait(false);

		// Assert
		AssertErrors(run, "Workflow failed: startup_failure");
	}

	[TestMethod]
	public async Task AnotherApiFailureLeavesTheErrorsEmptySoTheNextUpdateRetries()
	{
		// Arrange
		FakeJobsClient jobs = new([]) { ListFailure = ApiFailure(HttpStatusCode.InternalServerError) };
		Run run = FailedRun();

		// Act
		await GitHub.FetchRunErrorsAsync(jobs, run, Concluded(WorkflowRunConclusion.Failure)).ConfigureAwait(false);

		// Assert
		Assert.IsEmpty(run.Errors);
		Assert.IsTrue(GitHub.ShouldFetchRunErrors(run.Status, RunStatus.Failure, run.Errors));
	}

	[TestMethod]
	public async Task ARateLimitWhileListingJobsReachesTheRequestWrapper()
	{
		// Arrange
		ApiException rateLimited = ApiFailure(HttpStatusCode.Forbidden, new Dictionary<string, string> { ["X-RateLimit-Remaining"] = "0" });
		FakeJobsClient jobs = new([]) { ListFailure = rateLimited };

		// Act / Assert
		ApiException thrown = await Assert.ThrowsExactlyAsync<ApiException>(() => GitHub.FetchRunErrorsAsync(jobs, FailedRun(), Concluded(WorkflowRunConclusion.Failure))).ConfigureAwait(false);
		Assert.AreSame(rateLimited, thrown);
	}

	[TestMethod]
	public async Task ARateLimitWhileReadingALogReachesTheRequestWrapper()
	{
		// Arrange
		FakeJobsClient jobs = new([Job(1, "build", WorkflowJobConclusion.Failure)]) { LogFailure = ApiFailure(HttpStatusCode.TooManyRequests) };

		// Act / Assert
		await Assert.ThrowsExactlyAsync<ApiException>(() => GitHub.FetchRunErrorsAsync(jobs, FailedRun(), Concluded(WorkflowRunConclusion.Failure))).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task AnotherFailureReadingALogFallsBackToTheJob()
	{
		// Arrange
		FakeJobsClient jobs = new([Job(1, "build", WorkflowJobConclusion.Failure)]) { LogFailure = ApiFailure(HttpStatusCode.InternalServerError) };
		Run run = FailedRun();

		// Act
		await GitHub.FetchRunErrorsAsync(jobs, run, Concluded(WorkflowRunConclusion.Failure)).ConfigureAwait(false);

		// Assert
		AssertErrors(run, "[build] Failed");
	}
}
