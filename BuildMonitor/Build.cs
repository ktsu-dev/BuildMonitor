namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;

using ktsu.StrongStrings;

internal sealed record class BuildName : StrongStringAbstract<BuildName> { }
internal sealed record class BuildId : StrongStringAbstract<BuildId> { }

internal class Build
{
	private const int NumRecentRuns = 10;

	public BuildName Name { get; set; } = new();
	public BuildId Id { get; set; } = new();
	public Owner Owner { get; set; } = new();
	public Repository Repository { get; set; } = new();
	public bool Enabled { get; set; }
	public ConcurrentDictionary<RunId, Run> Runs { get; init; } = [];
	public DateTimeOffset LastStarted { get; set; }
	public DateTimeOffset LastUpdated { get; set; }
	public TimeSpan LastDuration => LastUpdated - LastStarted;
	public RunStatus LastStatus { get; set; }
	internal bool IsOngoing => !Runs.IsEmpty && LastStatus is RunStatus.Pending or RunStatus.Running;
	internal TimeSpan CalculateEstimatedDuration()
	{
		var recentRuns = Runs.Values.Where(r => r.Status != RunStatus.Canceled).OrderByDescending(r => r.Started).Skip(1).Take(NumRecentRuns).ToList();
		return recentRuns.Count == 0
			? TimeSpan.Zero
			: TimeSpan.FromSeconds(recentRuns.Average(r => r.Duration.TotalSeconds));
	}

	internal TimeSpan CalculateETA()
	{
		var estimate = CalculateEstimatedDuration();
		var duration = IsOngoing ? DateTimeOffset.UtcNow - LastStarted : LastDuration;
		return duration < estimate ? estimate - duration : TimeSpan.Zero;
	}

	internal Run CreateRun(RunName name) => CreateRun(name, (RunId)(string)name);
	internal Run CreateRun(RunName name, RunId id)
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

	internal void UpdateFromRun(Run run)
	{
		if (run.LastUpdated > LastUpdated)
		{
			LastUpdated = run.LastUpdated;
			LastStarted = run.Started;
			LastStatus = run.Status;
		}
	}
}
