namespace ktsu.io.BuildMonitor;

using ktsu.io.StrongStrings;

public sealed record class RunName : StrongStringAbstract<RunName> { }
public sealed record class RunId : StrongStringAbstract<RunId> { }

public enum RunStatus
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
}
