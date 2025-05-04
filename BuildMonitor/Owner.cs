// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;

using ktsu.StrongStrings;

internal sealed record class OwnerName : StrongStringAbstract<OwnerName> { }
internal sealed record class OwnerId : StrongStringAbstract<OwnerId> { }

internal class Owner
{
	public OwnerName Name { get; init; } = new();
	public OwnerId Id { get; init; } = new();
	public BuildProvider BuildProvider { get; init; } = null!; // only instantiate this via the Create method
	public bool Enabled { get; set; }
	public ConcurrentDictionary<RepositoryId, Repository> Repositories { get; init; } = [];

	internal Repository CreateRepository(RepositoryName name) => CreateRepository(name, (RepositoryId)(string)name);
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
