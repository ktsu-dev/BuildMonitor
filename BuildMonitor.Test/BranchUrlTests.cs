// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using ktsu.Semantics.Strings;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers "Open Branch URL" and "Copy Branch URL". The branch name went into the URL unescaped, so a
/// <c>#</c> started a fragment and a <c>%</c> read as a percent-escape, and the link pointed at the
/// wrong branch or a 404 (ktsu-dev/BuildMonitor#386).
/// </summary>
[TestClass]
public sealed class BranchUrlTests
{
	private sealed class StubProvider : BuildProvider
	{
		internal override BuildProviderName Name { get; } = "Stub".As<BuildProviderName>();

		internal override Task UpdateRepositoriesAsync(Owner owner) => Task.CompletedTask;
		internal override Task UpdateBuildsAsync(Repository repository) => Task.CompletedTask;
		internal override Task UpdateBuildAsync(Build build) => Task.CompletedTask;
		internal override Task UpdateRunAsync(Run run) => Task.CompletedTask;
	}

	private static Build CreateBuild()
	{
		Owner owner = new StubProvider().CreateOwner("o".As<OwnerName>());
		Repository repository = owner.CreateRepository("r".As<RepositoryName>());
		return repository.CreateBuild("build".As<BuildName>());
	}

	[TestMethod]
	[DataRow("feature/foo", "https://github.com/o/r/tree/feature/foo")]
	[DataRow("main", "https://github.com/o/r/tree/main")]
	[DataRow("fix/#123-crash", "https://github.com/o/r/tree/fix/%23123-crash")]
	[DataRow("feature/50%", "https://github.com/o/r/tree/feature/50%25")]
	public void BranchUrlEscapesEachSegmentAndKeepsSeparators(string branch, string expected)
	{
		// Act
		string url = BuildMonitor.GetBranchUrl(CreateBuild(), branch.As<BranchName>());

		// Assert
		Assert.AreEqual(expected, url);
	}
}
