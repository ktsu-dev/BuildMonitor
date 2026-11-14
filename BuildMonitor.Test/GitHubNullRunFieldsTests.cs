// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using System.Threading.Tasks;

using ktsu.Semantics.Strings;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Octokit;
using Octokit.Internal;

/// <summary>
/// Tests reading a GitHub workflow run whose nullable fields are null.
/// </summary>
/// <remarks>
/// GitHub sends a null <c>head_branch</c> for a run whose head repository is gone, such as a pull
/// request from a deleted fork, and its schema allows a null <c>name</c>. Both were converted without
/// a null check, which threw after the run had already been stored with the default Pending status,
/// so the build kept a row that never completed and stayed at High polling priority
/// (ktsu-dev/BuildMonitor#400).
/// </remarks>
[TestClass]
public sealed class GitHubNullRunFieldsTests
{
	private static Build NewBuild() => new()
	{
		Id = "7".As<BuildId>(),
		Name = "CI".As<BuildName>(),
		Owner = new() { Name = "ktsu-dev".As<OwnerName>() },
		Repository = new() { Name = "BuildMonitor".As<RepositoryName>() },
	};

	private static WorkflowRun CompletedRun(string headBranch, string name) =>
		new SimpleJsonSerializer().Deserialize<WorkflowRun>($$"""
			{
				"id": 1001,
				"name": {{name}},
				"head_branch": {{headBranch}},
				"status": "completed",
				"conclusion": "success",
				"run_started_at": "2026-11-05T00:00:00Z",
				"updated_at": "2026-11-05T00:05:00Z"
			}
			""");

	[TestMethod]
	[DataRow("null", "\"CI\"", DisplayName = "null head_branch")]
	[DataRow("\"main\"", "null", DisplayName = "null name")]
	[DataRow("null", "null", DisplayName = "both null")]
	public async Task ARunWithNullFieldsIsStoredAsCompleted(string headBranch, string name)
	{
		// Arrange
		Build build = NewBuild();
		WorkflowRun gitHubRun = CompletedRun(headBranch, name);

		// Act
		await new GitHub().UpdateBuildRunAsync(build, gitHubRun).ConfigureAwait(false);

		// Assert
		Assert.HasCount(1, build.Runs);
		Run run = build.Runs["1001".As<RunId>()];
		Assert.AreEqual(RunStatus.Success, run.Status);
		Assert.IsFalse(run.IsOngoing);
		Assert.IsFalse(build.HasOngoingRun, "A completed run must not pin its build at High priority");
	}

	[TestMethod]
	public async Task ANullHeadBranchGivesARunWithNoBranch()
	{
		// Arrange
		Build build = NewBuild();

		// Act
		await new GitHub().UpdateBuildRunAsync(build, CompletedRun("null", "\"CI\"")).ConfigureAwait(false);

		// Assert
		Assert.AreEqual(string.Empty, build.Runs["1001".As<RunId>()].Branch.ToString());
	}

	[TestMethod]
	public async Task ANullNameTakesTheWorkflowsName()
	{
		// Arrange
		Build build = NewBuild();

		// Act
		await new GitHub().UpdateBuildRunAsync(build, CompletedRun("\"main\"", "null")).ConfigureAwait(false);

		// Assert
		Assert.AreEqual("CI", build.Runs["1001".As<RunId>()].Name.ToString());
	}

	[TestMethod]
	public async Task AKnownRunIsUpdatedInPlace()
	{
		// Arrange
		Build build = NewBuild();
		Run stored = build.CreateRun("CI".As<RunName>(), "1001".As<RunId>());
		build.Runs.TryAdd(stored.Id, stored);

		// Act
		await new GitHub().UpdateBuildRunAsync(build, CompletedRun("null", "\"CI\"")).ConfigureAwait(false);

		// Assert
		Assert.AreSame(stored, build.Runs["1001".As<RunId>()]);
		Assert.AreEqual(RunStatus.Success, stored.Status);
	}
}
