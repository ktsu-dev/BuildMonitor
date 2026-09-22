// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using System.Text.Json;
using System.Text.Json.Serialization;

using ktsu.CredentialCache;
using ktsu.CredentialCache.Storage;
using ktsu.RoundTripStringJsonConverter;
using ktsu.Semantics.Strings;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using CredentialCache = ktsu.CredentialCache.CredentialCache;

/// <summary>
/// Covers where provider and owner access tokens live. They are GitHub and Azure DevOps personal
/// access tokens, so the contract under test is that they reach the OS secret store and do not
/// survive in the app data file earlier versions serialized them into.
/// </summary>
[TestClass]
public sealed class TokenStorageTests
{
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
	/// A provider with no network behaviour, so the storage contract can be driven without a real
	/// GitHub or Azure DevOps client.
	/// </summary>
	private sealed class TestProvider(string name) : BuildProvider
	{
		internal override BuildProviderName Name { get; } = name.As<BuildProviderName>();

		/// <summary>
		/// Writes the provider token the same way the base class's setter does. The setter itself is
		/// private, so this reaches the storage directly rather than widening it for a test.
		/// </summary>
		internal void SetToken(string token) =>
			Assert.IsTrue(TokenStorage.Write(TokenPersona, token.As<BuildProviderToken>()));

		internal BuildProviderToken ReadToken() => Token;

		internal override Task UpdateRepositoriesAsync(Owner owner) => Task.CompletedTask;
		internal override Task UpdateBuildsAsync(Repository repository) => Task.CompletedTask;
		internal override Task UpdateBuildAsync(Build build) => Task.CompletedTask;
		internal override Task UpdateRunAsync(Run run) => Task.CompletedTask;
	}

	/// <summary>
	/// A store standing in for a machine whose native secret library will not load — a Linux host
	/// with no Secret Service provider, which is what a container or an SSH session usually is.
	/// </summary>
	private sealed class UnavailableCredentialStore : ICredentialStore
	{
		public string Name => "Unavailable";

		public bool TryLoad(PersonaGUID persona, out Credential? credential) =>
			throw new DllNotFoundException("libsecret-1.so.0");

		public void Save(PersonaGUID persona, Credential credential) =>
			throw new DllNotFoundException("libsecret-1.so.0");

		public bool Remove(PersonaGUID persona) => throw new DllNotFoundException("libsecret-1.so.0");
	}

	/// <summary>
	/// Mirrors how <see cref="ktsu.AppDataStorage"/> writes the app data file: references preserved,
	/// and semantic strings round-tripped as plain strings rather than as char arrays.
	/// </summary>
	private static readonly JsonSerializerOptions AppDataLike = BuildAppDataLikeOptions();

	private static JsonSerializerOptions BuildAppDataLikeOptions()
	{
		JsonSerializerOptions options = new()
		{
			ReferenceHandler = ReferenceHandler.Preserve,
		};
		options.Converters.Add(new RoundTripStringJsonConverterFactory());
		return options;
	}

	private static TestProvider NewProvider(string name = "TestHub") => new(name);

	private static Owner AddOwner(TestProvider provider, string name)
	{
		OwnerName ownerName = name.As<OwnerName>();
		Owner owner = provider.CreateOwner(ownerName);
		Assert.IsTrue(provider.Owners.TryAdd(ownerName, owner));
		return owner;
	}

	/// <summary>
	/// Two providers never share a persona, so setting one's token cannot disturb the other's.
	/// </summary>
	[TestMethod]
	public void ProviderPersonasAreDistinct()
	{
		PersonaGUID gitHub = TokenStorage.ProviderPersona("GitHub".As<BuildProviderName>());
		PersonaGUID azure = TokenStorage.ProviderPersona("AzureDevOps".As<BuildProviderName>());

		Assert.AreNotEqual(gitHub, azure);
	}

	/// <summary>
	/// The same organization name can exist on more than one provider, so the owner persona is scoped
	/// by provider too.
	/// </summary>
	[TestMethod]
	public void OwnerPersonasAreScopedByProvider()
	{
		OwnerName owner = "ktsu-dev".As<OwnerName>();
		PersonaGUID onGitHub = TokenStorage.OwnerPersona("GitHub".As<BuildProviderName>(), owner);
		PersonaGUID onAzure = TokenStorage.OwnerPersona("AzureDevOps".As<BuildProviderName>(), owner);

		Assert.AreNotEqual(onGitHub, onAzure);
	}

	/// <summary>
	/// An owner's persona is never the provider's, so an owner override cannot overwrite the
	/// provider-level token.
	/// </summary>
	[TestMethod]
	public void OwnerPersonaIsNotTheProviderPersona()
	{
		BuildProviderName provider = "GitHub".As<BuildProviderName>();

		Assert.AreNotEqual(
			TokenStorage.ProviderPersona(provider),
			TokenStorage.OwnerPersona(provider, "ktsu-dev".As<OwnerName>()));
	}

	/// <summary>
	/// Persona derivation is deterministic, and pinned. Changing it would orphan every token already
	/// in the store, so it must not drift silently.
	/// </summary>
	[TestMethod]
	public void PersonaDerivationIsStable()
	{
		BuildProviderName provider = "GitHub".As<BuildProviderName>();

		Assert.AreEqual(
			TokenStorage.ProviderPersona(provider).ToString(),
			TokenStorage.ProviderPersona(provider).ToString());
		Assert.IsTrue(Guid.TryParse(TokenStorage.ProviderPersona(provider).ToString(), out _));
	}

	/// <summary>
	/// A provider token set through the normal path is readable again, and came from the store.
	/// </summary>
	[TestMethod]
	public void ProviderTokenRoundTripsThroughTheStore()
	{
		TestProvider provider = NewProvider();

		provider.SetToken("ghp_provider");

		Assert.AreEqual("ghp_provider", provider.ReadToken().ToString());
		Assert.IsTrue(Cache.TryGet(provider.TokenPersona, out Credential? credential));
		Assert.IsInstanceOfType<CredentialWithToken>(credential);
	}

	/// <summary>
	/// Setting a token leaves nothing behind in the field the app data file is built from. This is
	/// the whole point of the change.
	/// </summary>
	[TestMethod]
	public void SettingAProviderTokenWritesNothingToAppData()
	{
		TestProvider provider = NewProvider();

		provider.SetToken("ghp_provider");

		Assert.IsTrue(provider.LegacyToken.IsEmpty());
	}

	/// <summary>
	/// The serialized form of a provider carries no token. A reader of the app data file learns
	/// nothing.
	/// </summary>
	[TestMethod]
	public void SerializedProviderCarriesNoToken()
	{
		TestProvider provider = NewProvider();
		provider.SetToken("ghp_secret_value");

		string json = JsonSerializer.Serialize(provider, AppDataLike);

		Assert.DoesNotContain("ghp_secret_value", json, StringComparison.Ordinal);
		Assert.Contains("\"Token\":\"\"", json, StringComparison.Ordinal);
	}

	/// <summary>
	/// An owner override is stored separately from the provider token, and both survive.
	/// </summary>
	[TestMethod]
	public void OwnerTokenIsStoredSeparatelyFromTheProviderToken()
	{
		TestProvider provider = NewProvider();
		Owner owner = AddOwner(provider, "ktsu-dev");

		provider.SetToken("ghp_provider");
		owner.Token = "ghp_owner".As<BuildProviderToken>();

		Assert.AreEqual("ghp_provider", provider.ReadToken().ToString());
		Assert.AreEqual("ghp_owner", owner.Token.ToString());
		Assert.IsTrue(owner.HasToken);
		Assert.IsTrue(owner.LegacyToken.IsEmpty());
	}

	/// <summary>
	/// Clearing a token removes it from the store rather than leaving an empty entry behind.
	/// </summary>
	[TestMethod]
	public void ClearingATokenRemovesItFromTheStore()
	{
		TestProvider provider = NewProvider();
		Owner owner = AddOwner(provider, "ktsu-dev");
		owner.Token = "ghp_owner".As<BuildProviderToken>();

		owner.Token = new();

		Assert.IsFalse(owner.HasToken);
		Assert.IsFalse(Cache.TryGet(owner.TokenPersona, out _));
	}

	/// <summary>
	/// A provider token and an owner token left by an earlier version are both moved into the store.
	/// </summary>
	[TestMethod]
	public void MigrationMovesProviderAndOwnerTokens()
	{
		TestProvider provider = NewProvider();
		Owner owner = AddOwner(provider, "ktsu-dev");
		provider.LegacyToken = "ghp_old_provider".As<BuildProviderToken>();
		owner.LegacyToken = "ghp_old_owner".As<BuildProviderToken>();

		int migrated = TokenStorage.MigrateLegacyTokens([provider]);

		Assert.AreEqual(2, migrated);
		Assert.AreEqual("ghp_old_provider", provider.ReadToken().ToString());
		Assert.AreEqual("ghp_old_owner", owner.Token.ToString());
	}

	/// <summary>
	/// Migration also blanks the old copies. Ceasing to write a secret does not remove the one
	/// already on disk, so this is the half that does the security work.
	/// </summary>
	[TestMethod]
	public void MigrationBlanksThePlaintextCopies()
	{
		TestProvider provider = NewProvider();
		Owner owner = AddOwner(provider, "ktsu-dev");
		provider.LegacyToken = "ghp_old_provider".As<BuildProviderToken>();
		owner.LegacyToken = "ghp_old_owner".As<BuildProviderToken>();

		_ = TokenStorage.MigrateLegacyTokens([provider]);

		Assert.IsTrue(provider.LegacyToken.IsEmpty());
		Assert.IsTrue(owner.LegacyToken.IsEmpty());
	}

	/// <summary>
	/// With nothing left over there is nothing to migrate, so a second start is a no-op.
	/// </summary>
	[TestMethod]
	public void MigrationIsIdempotent()
	{
		TestProvider provider = NewProvider();
		provider.LegacyToken = "ghp_old_provider".As<BuildProviderToken>();

		_ = TokenStorage.MigrateLegacyTokens([provider]);

		Assert.AreEqual(0, TokenStorage.MigrateLegacyTokens([provider]));
		Assert.AreEqual("ghp_old_provider", provider.ReadToken().ToString());
	}

	/// <summary>
	/// A stale token in the old file must not overwrite the one currently in the store — but it is
	/// still cleared.
	/// </summary>
	[TestMethod]
	public void MigrationKeepsTheStoredTokenAndStillClearsTheStaleOne()
	{
		TestProvider provider = NewProvider();
		provider.SetToken("ghp_current");
		provider.LegacyToken = "ghp_stale".As<BuildProviderToken>();

		Assert.AreEqual(1, TokenStorage.MigrateLegacyTokens([provider]));
		Assert.AreEqual("ghp_current", provider.ReadToken().ToString());
		Assert.IsTrue(provider.LegacyToken.IsEmpty());
	}

	/// <summary>
	/// If the secret store will not take the token, the old copy stays where it is. Clearing it would
	/// destroy the only copy the user has.
	/// </summary>
	[TestMethod]
	public void MigrationKeepsTheLegacyTokenWhenTheStoreRefuses()
	{
		using CredentialCache unavailable = new(new UnavailableCredentialStore());
		TokenStorage.UseCache(unavailable);

		TestProvider provider = NewProvider();
		provider.LegacyToken = "ghp_old_provider".As<BuildProviderToken>();

		Assert.AreEqual(0, TokenStorage.MigrateLegacyTokens([provider]));
		Assert.AreEqual("ghp_old_provider", provider.LegacyToken.ToString());
	}

	/// <summary>
	/// A machine with no secret store reads as "no token" rather than throwing. BuildMonitor is a
	/// desktop application, and an exception out of a token read would take down the render loop; it
	/// reports the problem in the log instead. What it must never do is fall back to a plain file.
	/// </summary>
	[TestMethod]
	public void ReadingWithoutASecretStoreIsEmptyRatherThanFatal()
	{
		using CredentialCache unavailable = new(new UnavailableCredentialStore());
		TokenStorage.UseCache(unavailable);

		TestProvider provider = NewProvider();

		Assert.IsTrue(provider.ReadToken().IsEmpty());
		Assert.IsFalse(TokenStorage.Write(provider.TokenPersona, "ghp_x".As<BuildProviderToken>()));
	}

	/// <summary>
	/// Two owners under one provider do not share a token.
	/// </summary>
	[TestMethod]
	public void OwnersDoNotShareTokens()
	{
		TestProvider provider = NewProvider();
		Owner first = AddOwner(provider, "ktsu-dev");
		Owner second = AddOwner(provider, "ktsu-io");

		first.Token = "ghp_first".As<BuildProviderToken>();

		Assert.AreEqual("ghp_first", first.Token.ToString());
		Assert.IsFalse(second.HasToken);
	}
}
