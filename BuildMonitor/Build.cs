// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;
using System.Collections.Generic;
using ktsu.Semantics.Strings;

internal sealed record class BuildName : SemanticString<BuildName> { }
internal sealed record class BuildId : SemanticString<BuildId> { }

internal sealed class Build
{
	private const int NumRecentRuns = 10;
	private readonly Lock _updateLock = new();

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
		List<Run> recentRuns = [.. Runs.Values.Where(r => r.Status != RunStatus.Canceled).OrderByDescending(r => r.Started).Skip(1).Take(NumRecentRuns)];
		return recentRuns.Count == 0
			? TimeSpan.Zero
			: TimeSpan.FromSeconds(recentRuns.Average(r => r.Duration.TotalSeconds));
	}

	internal TimeSpan CalculateETA()
	{
		TimeSpan estimate = CalculateEstimatedDuration();
		TimeSpan duration = IsOngoing ? DateTimeOffset.UtcNow - LastStarted : LastDuration;
		return duration < estimate ? estimate - duration : TimeSpan.Zero;
	}

	internal Run CreateRun(RunName name) => CreateRun(name, name.As<RunId>());
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
		lock (_updateLock)
		{
			if (run.LastUpdated > LastUpdated)
			{
				LastUpdated = run.LastUpdated;
				LastStarted = run.Started;
				LastStatus = run.Status;
			}
		}
	}
}
