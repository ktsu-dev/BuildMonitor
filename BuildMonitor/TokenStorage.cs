// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor;

using System.Security.Cryptography;
using System.Text;

using ktsu.CredentialCache;
using ktsu.CredentialCache.Storage;
using ktsu.Semantics.Strings;

using CredentialCache = ktsu.CredentialCache.CredentialCache;

/// <summary>
/// Holds provider and owner access tokens in the operating system's secret store — Windows
/// Credential Manager, macOS Keychain, or libsecret (Secret Service) on Linux — rather than in
/// <see cref="AppData"/>'s JSON file.
/// </summary>
/// <remarks>
/// The tokens are GitHub and Azure DevOps personal access tokens, usually scoped to
/// <c>repo</c>/<c>workflow</c>. <see cref="ktsu.AppDataStorage"/> serializes the whole app data
/// object to an unencrypted file on every save, which is not somewhere a PAT belongs.
/// </remarks>
internal static class TokenStorage
{
	/// <summary>
	/// Scopes BuildMonitor's entries within the OS secret store so they cannot collide with another
	/// ktsu tool's credentials on a shared host.
	/// </summary>
	internal const string CredentialServiceName = "ktsu.BuildMonitor";

	/// <summary>
	/// Prefixed to every persona seed. Versioned because changing the derivation would orphan every
	/// token already in the store, so a future change has to be a deliberate, visible one.
	/// </summary>
	private const string PersonaNamespace = "ktsu.BuildMonitor/v1/";

	private const string UnavailableMessage =
		"No usable OS secret store was found, so access tokens cannot be read or saved. On Linux this " +
		"usually means no Secret Service provider (libsecret with GNOME Keyring or KWallet) is installed " +
		"and unlocked. BuildMonitor will not fall back to writing tokens to a plain file.";

	private static readonly Lazy<CredentialCache> LazyDefaultCache =
		new(() => new CredentialCache(CredentialStoreFactory.CreateDefault(CredentialServiceName)));

	private static int _unavailableReported;

	/// <summary>
	/// The cache substituted by <see cref="UseCache"/>, or <see langword="null"/> for the default.
	/// </summary>
	private static CredentialCache? InjectedCache { get; set; }

	/// <summary>
	/// Gets the cache backing this storage.
	/// </summary>
	/// <remarks>
	/// Built directly rather than taken from <see cref="CredentialCache.Instance"/> so the store
	/// carries BuildMonitor's own service name; the singleton can only ever use the library default.
	/// </remarks>
	internal static CredentialCache Cache => InjectedCache ?? LazyDefaultCache.Value;

	/// <summary>
	/// Substitutes the backing cache. Test seam; pass <see langword="null"/> to restore the default.
	/// </summary>
	internal static void UseCache(CredentialCache? cache)
	{
		InjectedCache = cache;
		_ = Interlocked.Exchange(ref _unavailableReported, 0);
	}

	/// <summary>
	/// Derives the persona holding a provider-level token.
	/// </summary>
	internal static PersonaGUID ProviderPersona(BuildProviderName provider) =>
		DerivePersona($"provider/{provider}");

	/// <summary>
	/// Derives the persona holding an owner's override token. Scoped by provider as well as owner,
	/// because the same organization name can exist on more than one provider.
	/// </summary>
	internal static PersonaGUID OwnerPersona(BuildProviderName provider, OwnerName owner) =>
		DerivePersona($"provider/{provider}/owner/{owner}");

	/// <summary>
	/// Reads the token stored under <paramref name="persona"/>, or an empty token when there is none.
	/// </summary>
	internal static BuildProviderToken Read(PersonaGUID persona)
	{
		try
		{
			return Cache.TryGet(persona, out Credential? credential) && credential is CredentialWithToken token
				? token.Token.ToString().As<BuildProviderToken>()
				: new();
		}
		catch (Exception ex) when (IsUnavailable(ex))
		{
			ReportUnavailable(ex);
			return new();
		}
	}

	/// <summary>
	/// Stores <paramref name="token"/> under <paramref name="persona"/>, removing the entry when the
	/// token is empty.
	/// </summary>
	/// <returns><see langword="false"/> when the secret store refused the write.</returns>
	internal static bool Write(PersonaGUID persona, BuildProviderToken token)
	{
		try
		{
			if (token.IsEmpty())
			{
				_ = Cache.Remove(persona);
				return true;
			}

			Cache.AddOrReplace(persona, new CredentialWithToken
			{
				Token = SemanticString<CredentialToken>.Create(token.ToString()),
			});
			return true;
		}
		catch (Exception ex) when (IsUnavailable(ex))
		{
			ReportUnavailable(ex);
			return false;
		}
	}

	/// <summary>
	/// Moves tokens left in <see cref="AppData"/> by earlier versions into the secret store and
	/// blanks them where they were.
	/// </summary>
	/// <returns>How many tokens were moved.</returns>
	internal static int MigrateLegacyTokens(IEnumerable<BuildProvider> providers)
	{
		Ensure.NotNull(providers);

		int migrated = 0;

		foreach (BuildProvider provider in providers)
		{
			if (MigrateToken(provider.TokenPersona, provider.LegacyToken, () => provider.LegacyToken = new()))
			{
				migrated++;
			}

			foreach (Owner owner in provider.Owners.Values)
			{
				PersonaGUID persona = OwnerPersona(provider.Name, owner.Name);
				if (MigrateToken(persona, owner.LegacyToken, () => owner.LegacyToken = new()))
				{
					migrated++;
				}
			}
		}

		return migrated;
	}

	/// <summary>
	/// Moves one token, leaving the old copy in place if the secret store will not take it.
	/// </summary>
	/// <remarks>
	/// A token already in the store wins over a legacy one, so a stale copy in the old file cannot
	/// overwrite a current credential — but the stale copy is still cleared, because ceasing to write
	/// a secret does not remove the one already on disk.
	/// </remarks>
	private static bool MigrateToken(PersonaGUID persona, BuildProviderToken legacy, Action clearLegacy)
	{
		if (legacy.IsEmpty())
		{
			return false;
		}

		if (Read(persona).IsEmpty() && !Write(persona, legacy))
		{
			return false;
		}

		clearLegacy();
		return true;
	}

	private static PersonaGUID DerivePersona(string seed)
	{
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(PersonaNamespace + seed));
		return SemanticString<PersonaGUID>.Create(new Guid(hash.AsSpan(0, 16)).ToString());
	}

	/// <summary>
	/// Recognises a machine with no usable secret store: the factory refusing the platform, the
	/// native library failing to resolve, or the store itself reporting a failure.
	/// </summary>
	private static bool IsUnavailable(Exception exception) =>
		exception is PlatformNotSupportedException
			or DllNotFoundException
			or EntryPointNotFoundException
			or CredentialStoreException;

	/// <summary>
	/// Logs the unavailable store once per process. Tokens are read on request paths that run every
	/// few seconds, so reporting each failure would bury the log.
	/// </summary>
	private static void ReportUnavailable(Exception exception)
	{
		if (Interlocked.Exchange(ref _unavailableReported, 1) == 0)
		{
			Log.Error($"{UnavailableMessage} ({exception.Message})");
		}
	}
}
