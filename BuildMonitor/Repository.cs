namespace ktsu.io.BuildMonitor;

using ktsu.io.StrongStrings;

public sealed record class RepositoryName : StrongStringAbstract<RepositoryName> { }
public sealed record class RepositoryId : StrongStringAbstract<RepositoryId> { }

internal class Repository
{
	public RepositoryName Name { get; set; } = new();
	public RepositoryId Id { get; set; } = new();
	public Owner Owner { get; set; } = new();
	public bool Enabled { get; set; }
	public Dictionary<BuildId, Build> Builds { get; init; } = [];

	internal Build CreateBuild(BuildName name) => CreateBuild(name, (BuildId)(string)name);
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
