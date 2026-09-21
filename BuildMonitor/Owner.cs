// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;
using System.Text.Json.Serialization;

using ktsu.CredentialCache;
using ktsu.Semantics.Strings;

internal sealed record class OwnerName : SemanticString<OwnerName> { }
internal sealed record class OwnerId : SemanticString<OwnerId> { }

internal sealed class Owner
{
	public OwnerName Name { get; init; } = new();
	public OwnerId Id { get; init; } = new();
	public BuildProvider BuildProvider { get; init; } = null!; // only instantiate this via the Create method
	public bool Enabled { get; set; }
	public ConcurrentDictionary<RepositoryId, Repository> Repositories { get; init; } = [];

	/// <summary>
	/// The owner token as earlier versions persisted it: plaintext, in the app data file.
	/// </summary>
	/// <remarks>
	/// Retained under its original JSON name only so
	/// <see cref="TokenStorage.MigrateLegacyTokens"/> can move an existing token into the OS secret
	/// store and blank it here. Nothing writes a token here any more.
	/// </remarks>
	[JsonInclude]
	[JsonPropertyName("Token")]
	internal BuildProviderToken LegacyToken { get; set; } = new();

	/// <summary>
	/// The persona this owner's token is stored under.
	/// </summary>
	[JsonIgnore]
	internal PersonaGUID TokenPersona => TokenStorage.OwnerPersona(BuildProvider.Name, Name);

	/// <summary>
	/// Optional token for this specific owner. If set, overrides the provider-level token.
	/// Useful for accessing private repositories in different organizations.
	/// </summary>
	/// <remarks>
	/// Kept in the OS secret store, not in the app data file.
	/// </remarks>
	[JsonIgnore]
	public BuildProviderToken Token
	{
		get => TokenStorage.Read(TokenPersona);
		internal set => _ = TokenStorage.Write(TokenPersona, value);
	}

	/// <summary>
	/// Returns true if this owner has a specific token configured.
	/// </summary>
	[JsonIgnore]
	public bool HasToken => !Token.IsEmpty();

	internal Repository CreateRepository(RepositoryName name) => CreateRepository(name, name.As<RepositoryId>());
	internal Repository CreateRepository(RepositoryName name, RepositoryId id)
	{
		return new()
		{
			Name = name,
			Id = id,
			Owner = this,
			Enabled = true
		};
	}
}
