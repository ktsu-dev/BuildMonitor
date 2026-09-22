// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Octokit;

/// <summary>
/// Tests whether one GitHub API request reports having succeeded.
/// </summary>
/// <remarks>
/// <c>MakeGitHubRequestAsync</c> absorbs the failures this provider knows how to handle: it sets the
/// status and logs, but does not rethrow. That is deliberate -- a polling update should not tear
/// down over a rate limit -- but it means reaching the line after the call says nothing about
/// whether the call happened. The three workflow actions assumed it did and returned
/// <see langword="true"/> unconditionally, so cancelling a workflow while rate limited, or after the
/// token was revoked, reported success and force-refreshed the build while nothing had happened
/// server-side.
///
/// The request body is a <see cref="Func{TResult}"/> the caller supplies, so the failures can be
/// injected directly and the real method driven, rather than a rule being lifted out of it.
/// </remarks>
[TestClass]
public sealed class GitHubRequestOutcomeTests
{
	private const string RequestName = "test/request";

	/// <summary>
	/// A response built to order. Octokit's own <c>Response</c> is internal, and the provider reads
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

	private static Task<bool> RunFailing(GitHub provider, Exception failure) =>
		provider.MakeGitHubRequestAsync(RequestName, () => Task.FromException(failure));

	[TestMethod]
	public async Task ASuccessfulRequestReportsSuccess()
	{
		GitHub provider = new();

		bool succeeded = await provider.MakeGitHubRequestAsync(RequestName, () => Task.CompletedTask).ConfigureAwait(false);

		Assert.IsTrue(succeeded);
		Assert.AreEqual(ProviderStatus.OK, provider.Status);
	}

	/// <summary>
	/// The headline case from the issue: rate limited, so the request never reached GitHub.
	/// </summary>
	[TestMethod]
	public async Task ARateLimited403ReportsFailure()
	{
		GitHub provider = new();
		ApiException rateLimited = ApiFailure(
			HttpStatusCode.Forbidden,
			new Dictionary<string, string> { ["X-RateLimit-Remaining"] = "0" });

		bool succeeded = await RunFailing(provider, rateLimited).ConfigureAwait(false);

		Assert.IsFalse(succeeded, "A request refused for rate limiting did not happen, and must not report success.");
		Assert.AreEqual(ProviderStatus.RateLimited, provider.Status);
	}

	/// <summary>
	/// The other headline case: a plain 403, which is what a just-revoked token looks like.
	/// </summary>
	[TestMethod]
	public async Task APlain403ReportsFailure()
	{
		GitHub provider = new();

		bool succeeded = await RunFailing(provider, ApiFailure(HttpStatusCode.Forbidden)).ConfigureAwait(false);

		Assert.IsFalse(succeeded, "A request refused for lack of authorization did not happen, and must not report success.");
		Assert.AreEqual(ProviderStatus.AuthFailed, provider.Status);
	}

	[TestMethod]
	public async Task A429ReportsFailure()
	{
		GitHub provider = new();

		bool succeeded = await RunFailing(provider, ApiFailure(HttpStatusCode.TooManyRequests)).ConfigureAwait(false);

		Assert.IsFalse(succeeded);
		Assert.AreEqual(ProviderStatus.RateLimited, provider.Status);
	}

	[TestMethod]
	public async Task AnAuthorizationExceptionReportsFailure()
	{
		GitHub provider = new();
		AuthorizationException unauthorized = new(new FakeResponse(HttpStatusCode.Unauthorized, new Dictionary<string, string>()));

		bool succeeded = await RunFailing(provider, unauthorized).ConfigureAwait(false);

		Assert.IsFalse(succeeded);
		Assert.AreEqual(ProviderStatus.AuthFailed, provider.Status);
	}

	[TestMethod]
	public async Task AConnectionErrorReportsFailure()
	{
		GitHub provider = new();

		bool succeeded = await RunFailing(provider, new HttpRequestException("connection reset")).ConfigureAwait(false);

		Assert.IsFalse(succeeded);
		Assert.AreEqual(ProviderStatus.Error, provider.Status);
	}

	private static Owner OwnerWithToken() => new()
	{
		Name = OwnerName.Create<OwnerName>("alpha"),
		Token = BuildProviderToken.Create<BuildProviderToken>("alpha-pat"),
	};

	/// <summary>
	/// The workflow actions themselves: a refused request must come back as a failure, not as the
	/// unconditional success they used to report.
	/// </summary>
	/// <remarks>
	/// The API call is a delegate, so the refusal is injected and nothing reaches the network. The
	/// owner carries its own token, which is what gets past the credential guard without the
	/// provider needing one.
	/// </remarks>
	[TestMethod]
	public async Task AWorkflowActionRefusedByTheApiReportsFailure()
	{
		GitHub provider = new();
		ApiException rateLimited = ApiFailure(
			HttpStatusCode.Forbidden,
			new Dictionary<string, string> { ["X-RateLimit-Remaining"] = "0" });

		bool succeeded = await provider.RunWorkflowActionAsync(
			OwnerWithToken(), RequestName, () => Task.FromException(rateLimited)).ConfigureAwait(false);

		Assert.IsFalse(succeeded, "A cancel or re-run the API refused must not be reported as having worked.");
		Assert.AreEqual(ProviderStatus.RateLimited, provider.Status);
	}

	[TestMethod]
	public async Task AWorkflowActionThatSucceedsReportsSuccess()
	{
		GitHub provider = new();

		bool succeeded = await provider.RunWorkflowActionAsync(
			OwnerWithToken(), RequestName, () => Task.CompletedTask).ConfigureAwait(false);

		Assert.IsTrue(succeeded);
	}

	/// <summary>
	/// A missing NotFound is still a failure, and is caught by the action rather than escaping.
	/// </summary>
	[TestMethod]
	public async Task AWorkflowActionOnAMissingRunReportsFailure()
	{
		GitHub provider = new();
		NotFoundException missing = new(new FakeResponse(HttpStatusCode.NotFound, new Dictionary<string, string>()));

		bool succeeded = await provider.RunWorkflowActionAsync(
			OwnerWithToken(), RequestName, () => Task.FromException(missing)).ConfigureAwait(false);

		Assert.IsFalse(succeeded);
	}

	[TestMethod]
	public async Task AWorkflowActionWithoutCredentialsReportsFailureWithoutCalling()
	{
		GitHub provider = new();
		bool called = false;

		bool succeeded = await provider.RunWorkflowActionAsync(
			new Owner { Name = OwnerName.Create<OwnerName>("beta") },
			RequestName,
			() =>
			{
				called = true;
				return Task.CompletedTask;
			}).ConfigureAwait(false);

		Assert.IsFalse(succeeded);
		Assert.IsFalse(called, "With no credentials there is nothing to ask GitHub.");
	}

	/// <summary>
	/// A status this provider has no handling for still propagates, so a caller that genuinely
	/// cannot continue is not quietly handed a <see langword="false"/> instead.
	/// </summary>
	[TestMethod]
	public async Task AnUnhandledStatusStillPropagates()
	{
		GitHub provider = new();

		await Assert.ThrowsExactlyAsync<ApiException>(
			() => RunFailing(provider, ApiFailure(HttpStatusCode.InternalServerError))).ConfigureAwait(false);
	}
}
