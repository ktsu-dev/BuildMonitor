// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers how in-flight requests mark a build as updating (ktsu-dev/BuildMonitor#351): a build's key
/// has to match whole segments of a request key, and a key shared by two concurrent requests has to
/// stay until both finish.
/// </summary>
[TestClass]
public sealed class ActiveRequestTests
{
	private const string BuildKey = "GitHub/ktsu-dev/BuildMonitor/Build";

	[TestMethod]
	public void ABuildMatchesItsOwnPollAndItsRunsPolls()
	{
		Assert.IsTrue(BuildMonitor.IsRequestForBuild(BuildKey, BuildKey));
		Assert.IsTrue(BuildMonitor.IsRequestForBuild($"{BuildKey}/Build #42", BuildKey));
	}

	[TestMethod]
	public void ABuildDoesNotMatchAnotherBuildItsNameIsAPrefixOf()
	{
		Assert.IsFalse(BuildMonitor.IsRequestForBuild($"{BuildKey} and Release", BuildKey));
		Assert.IsFalse(BuildMonitor.IsRequestForBuild($"{BuildKey} and Release/Build and Release", BuildKey));
		Assert.IsFalse(BuildMonitor.IsRequestForBuild($"{BuildKey}Docs", BuildKey));
	}

	[TestMethod]
	public async Task AKeySharedByTwoRequestsStaysUntilBothFinish()
	{
		// A unique key, because ActiveRequests is process-wide and other tests may run alongside.
		string key = $"{BuildKey}/{Guid.NewGuid():N}";
		TaskCompletionSource first = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource second = new(TaskCreationOptions.RunContinuationsAsynchronously);

		Task firstRequest = BuildMonitor.MakeRequestAsync(key, () => first.Task);
		Task secondRequest = BuildMonitor.MakeRequestAsync(key, () => second.Task);
		Assert.IsTrue(BuildMonitor.ActiveRequests.ContainsKey(key));

		first.SetResult();
		await firstRequest.ConfigureAwait(false);
		Assert.IsTrue(BuildMonitor.ActiveRequests.ContainsKey(key), "The second request is still in flight.");

		second.SetResult();
		await secondRequest.ConfigureAwait(false);
		Assert.IsFalse(BuildMonitor.ActiveRequests.ContainsKey(key));
	}
}
