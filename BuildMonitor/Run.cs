// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using ktsu.Semantics.Strings;

internal sealed record class RunName : SemanticString<RunName> { }
internal sealed record class RunId : SemanticString<RunId> { }
internal sealed record class BranchName : SemanticString<BranchName> { }

internal enum RunStatus
{
	Pending,
	Running,
	Canceled,
	Success,
	Failure,
}

internal sealed class Run
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
	public BranchName Branch { get; set; } = new();
	public IReadOnlyList<string> Errors { get; set; } = [];

	internal bool IsOngoing => Status is RunStatus.Pending or RunStatus.Running;

	internal TimeSpan CalculateETA()
	{
		// Use branch-specific estimation for more accurate ETA
		TimeSpan estimate = Build.CalculateEstimatedDuration(Branch);
		TimeSpan duration = IsOngoing ? DateTimeOffset.UtcNow - Started : Duration;
		return duration < estimate ? estimate - duration : TimeSpan.Zero;
	}
}
