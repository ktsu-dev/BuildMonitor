namespace ktsu.BuildMonitor;

using ktsu.io.StrongStrings;

internal sealed record class RunName : StrongStringAbstract<RunName> { }
internal sealed record class RunId : StrongStringAbstract<RunId> { }

internal enum RunStatus
{
	Pending,
	Running,
	Canceled,
	Success,
	Failure,
}

internal class Run
{
	public RunName Name { get; set; } = new();
	public RunId Id { get; set; } = new();
	public Owner Owner { get; set; } = new();
	public Repository Repository { get; set; } = new();
	public Build Build { get; set; } = new();
	public bool Enabled { get; set; }
	public RunStatus Status { get; set; }
	public DateTimeOffset Started { get; set; }
	public DateTimeOffset LastUpdated { get; set; }
	public TimeSpan Duration => LastUpdated - Started;

	internal bool IsOngoing => Status is RunStatus.Pending or RunStatus.Running;

	internal TimeSpan CalculateETA()
	{
		var estimate = Build.CalculateEstimatedDuration();
		var duration = IsOngoing ? DateTimeOffset.UtcNow - Started : Duration;
		return duration < estimate ? estimate - duration : TimeSpan.Zero;
	}
}
