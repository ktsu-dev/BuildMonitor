// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the part of the Azure DevOps session lookup that can run without a live organization.
/// </summary>
/// <remarks>
/// Building a session calls <c>VssConnection.GetClient&lt;T&gt;()</c>, which authenticates against
/// dev.azure.com and throws <c>VssUnauthorizedException</c> offline, so everything past the
/// credential check needs a real organization and a real token. The check itself does not, and it
/// is the branch that decides whether a connection is attempted at all.
/// </remarks>
[TestClass]
public sealed class AzureDevOpsSessionTests
{
	/// <summary>
	/// A provider with no credentials must report no session rather than attempting a connection.
	/// Reaching the factory here would try to authenticate against an organization named by an empty
	/// string, so this branch is what keeps an unconfigured provider off the network entirely.
	/// </summary>
	[TestMethod]
	public void AProviderWithNoCredentialsHasNoSession()
	{
		AzureDevOps provider = new();

		AzureDevOps.AzureDevOpsSession? session = provider.EnsureAzureDevOpsClients();

		Assert.IsNull(session);
	}

	/// <summary>
	/// Asking twice still attempts nothing, so a repeated update on an unconfigured provider cannot
	/// accumulate connections.
	/// </summary>
	[TestMethod]
	public void RepeatedLookupsOnAnUnconfiguredProviderStayNull()
	{
		AzureDevOps provider = new();

		Assert.IsNull(provider.EnsureAzureDevOpsClients());
		Assert.IsNull(provider.EnsureAzureDevOpsClients());
	}

	/// <summary>
	/// The provider reports the name the rest of the application keys its configuration off, which is
	/// also the JSON discriminator its credentials are persisted under.
	/// </summary>
	[TestMethod]
	public void TheProviderReportsItsName()
	{
		AzureDevOps provider = new();

		Assert.AreEqual(nameof(AzureDevOps), provider.Name.ToString());
	}
}
