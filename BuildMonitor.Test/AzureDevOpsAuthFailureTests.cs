// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using ktsu.CredentialCache.Storage;
using ktsu.Semantics.Strings;

using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using CredentialCache = ktsu.CredentialCache.CredentialCache;

/// <summary>
/// Covers a token that Azure DevOps rejects. Building the clients authenticates, and the rejection
/// arrives as <see cref="VssUnauthorizedException"/>, which is not a <c>VssServiceException</c>. The
/// provider caught only the latter, so the exception escaped, faulted the whole update loop, and the
/// bad token was never reported as AuthFailed (ktsu-dev/BuildMonitor#305).
/// </summary>
/// <remarks>
/// The token lives in the process-wide <see cref="TokenStorage"/>, so these run outside the parallel
/// phase rather than racing another class that swaps the same cache.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class AzureDevOpsAuthFailureTests
{
	private const string Organization = "contoso";
	private const string ExpiredToken = "expired";

	private CredentialCache Cache { get; set; } = null!;

	[TestInitialize]
	public void SetUp()
	{
		Cache = new CredentialCache(new InMemoryCredentialStore());
		TokenStorage.UseCache(Cache);
	}

	[TestCleanup]
	public void TearDown()
	{
		TokenStorage.UseCache(null);
		Cache.Dispose();
	}

	/// <summary>
	/// A provider configured with credentials whose session factory fails the way dev.azure.com does
	/// for an expired or revoked PAT.
	/// </summary>
	private static AzureDevOps CreateProviderWithRejectedToken()
	{
		AzureDevOps provider = new((_, _) => throw new VssUnauthorizedException("VS30063: You are not authorized to access https://dev.azure.com."))
		{
			AccountId = Organization.As<BuildProviderAccountId>(),
		};
		Assert.IsTrue(TokenStorage.Write(provider.TokenPersona, ExpiredToken.As<BuildProviderToken>()));
		return provider;
	}

	[TestMethod]
	public void ARejectedTokenYieldsNoSessionAndReportsAuthFailed()
	{
		// Arrange
		AzureDevOps provider = CreateProviderWithRejectedToken();

		// Act
		using CredentialedSessionCache<AzureDevOps.AzureDevOpsSession>.Lease? lease = provider.EnsureAzureDevOpsClients(out _);

		// Assert
		Assert.IsNull(lease);
		Assert.AreEqual(ProviderStatus.AuthFailed, provider.Status);
	}

	[TestMethod]
	public void ARejectedTokenClearsTheCredentials()
	{
		// Arrange
		AzureDevOps provider = CreateProviderWithRejectedToken();

		// Act
		_ = provider.EnsureAzureDevOpsClients(out _);

		// Assert
		Assert.IsTrue(provider.AccountId.IsEmpty(), "The organization should be cleared so the user is asked again");
		Assert.IsTrue(TokenStorage.Read(provider.TokenPersona).IsEmpty(), "The rejected token should be removed from the secret store");
	}

	[TestMethod]
	public async Task ARepositoryUpdateWithARejectedTokenDoesNotThrow()
	{
		// Arrange
		AzureDevOps provider = CreateProviderWithRejectedToken();
		Owner owner = new() { Name = "Project".As<OwnerName>() };

		// Act: an exception here is what faulted UpdateAsync and stopped GitHub polling with it.
		await provider.UpdateRepositoriesAsync(owner).ConfigureAwait(false);

		// Assert
		Assert.AreEqual(ProviderStatus.AuthFailed, provider.Status);
	}

	[TestMethod]
	public async Task AnUnauthorizedRequestReportsAuthFailed()
	{
		// Arrange
		AzureDevOps provider = CreateProviderWithRejectedToken();

		// Act
		await provider.MakeAzureDevOpsRequestAsync("test/unauthorized", () =>
			throw new VssUnauthorizedException("VS30063: You are not authorized.")).ConfigureAwait(false);

		// Assert
		Assert.AreEqual(ProviderStatus.AuthFailed, provider.Status);
		Assert.IsTrue(provider.AccountId.IsEmpty());
	}
}
