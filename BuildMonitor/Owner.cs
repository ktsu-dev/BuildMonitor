// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;

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
