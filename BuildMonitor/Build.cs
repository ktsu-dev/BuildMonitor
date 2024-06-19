namespace ktsu.io.BuildMonitor;

using ktsu.io.StrongStrings;

public sealed record class BuildName : StrongStringAbstract<BuildName> { }
public sealed record class BuildId : StrongStringAbstract<BuildId> { }

internal class Build
{
	private const int NumRecentRuns = 10;

	public BuildName Name { get; set; } = new();
	public BuildId Id { get; set; } = new();
	public Owner Owner { get; set; } = new();
	public Repository Repository { get; set; } = new();
	public bool Enabled { get; set; }
	public Dictionary<RunId, Run> Runs { get; init; } = [];
	public DateTimeOffset LastStarted { get; set; }
	public DateTimeOffset LastUpdated { get; set; }
	public TimeSpan LastDuration => LastUpdated - LastStarted;
	public RunStatus LastStatus { get; set; }
	public TimeSpan CalculateEstimatedDuration()
	{
		var recentRuns = Runs.Values.OrderByDescending(r => r.Started).Skip(1).Take(NumRecentRuns).ToList();
		return recentRuns.Count == 0
			? TimeSpan.Zero
			: TimeSpan.FromSeconds(recentRuns.Average(r => r.Duration.TotalSeconds));
	}

	public Run CreateRun(RunName name) => CreateRun(name, (RunId)(string)name);
	public Run CreateRun(RunName name, RunId id)
	{
		return new()
		{
			Name = name,
			Id = id,
			Build = this,
			Repository = Repository,
			Owner = Owner,
			Enabled = true
		};
	}
}
