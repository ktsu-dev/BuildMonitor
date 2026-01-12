// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;

using ktsu.Semantics.Strings;

internal sealed record class RepositoryName : SemanticString<RepositoryName> { }
internal sealed record class RepositoryId : SemanticString<RepositoryId> { }

internal sealed class Repository
{
	public RepositoryName Name { get; set; } = new();
	public RepositoryId Id { get; set; } = new();
	public Owner Owner { get; set; } = new();
	public bool Enabled { get; set; }
	public ConcurrentDictionary<BuildId, Build> Builds { get; init; } = [];

	internal Build CreateBuild(BuildName name) => CreateBuild(name, name.As<BuildId>());
	internal Build CreateBuild(BuildName name, BuildId id)
	{
		return new()
		{
			Name = name,
			Id = id,
			Repository = this,
			Owner = Owner,
			Enabled = true
		};
	}
}
