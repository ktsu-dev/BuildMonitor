// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;
using System.Text.Json.Serialization;

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
	/// Optional token for this specific owner. If set, overrides the provider-level token.
	/// Useful for accessing private repositories in different organizations.
	/// </summary>
	[JsonInclude]
	public BuildProviderToken Token { get; internal set; } = new();

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
