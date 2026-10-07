// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using ktsu.Semantics.Strings;
using ktsu.TextFilter;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the column filters as the table applies them: each column's filter, type and match
/// options come from the app data, with the defaults a new install starts with
/// (ktsu-dev/BuildMonitor#319).
/// </summary>
/// <remarks>
/// The filters live on the process-wide <see cref="BuildMonitor.AppData"/>, so these run outside the
/// parallel phase and put a fresh instance back afterwards.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ColumnFilterRowTests
{
	private static AppData Filters => BuildMonitor.AppData;

	[TestInitialize]
	public void SetUp() => BuildMonitor.AppData = new();

	[TestCleanup]
	public void TearDown() => BuildMonitor.AppData = new();

	private static Build MakeBuild(string owner, string repository, string name, RunStatus status, string branch, out BranchName branchName)
	{
		Owner buildOwner = new() { Name = owner.As<OwnerName>() };
		Repository buildRepository = new() { Name = repository.As<RepositoryName>(), Owner = buildOwner };
		Build build = new() { Name = name.As<BuildName>(), Owner = buildOwner, Repository = buildRepository };
		branchName = branch.As<BranchName>();
		Run run = new() { Id = "1".As<RunId>(), Build = build, Branch = branchName, Status = status };
		build.Runs[run.Id] = run;
		return build;
	}

	private static bool IsBranchShown(string branch)
	{
		Build build = MakeBuild("ktsu-dev", "BuildMonitor", "dotnet.yml", RunStatus.Success, branch, out BranchName branchName);
		return BuildMonitor.ShouldShowBuildBranch(build, branchName);
	}

	[TestMethod]
	public void ARegexBranchFilterMatchesOnlyThatBranch()
	{
		Filters.FilterBranch = "^main$";
		Filters.FilterBranchType = TextFilterType.Regex;

		Assert.IsTrue(IsBranchShown("main"));
		Assert.IsFalse(IsBranchShown("maintenance"));
	}

	[TestMethod]
	public void AFuzzyBranchFilterMatchesCharactersInOrder()
	{
		Filters.FilterBranch = "mn";
		Filters.FilterBranchType = TextFilterType.Fuzzy;

		Assert.IsTrue(IsBranchShown("main"));
		Assert.IsFalse(IsBranchShown("develop"));
	}

	[TestMethod]
	public void AGlobBranchExclusionHidesThatBranch()
	{
		Filters.FilterBranch = "-main";

		Assert.IsFalse(IsBranchShown("main"));
		Assert.IsTrue(IsBranchShown("develop"));
	}

	[TestMethod]
	public void EachBuildColumnFiltersOnItsOwnValue()
	{
		Build build = MakeBuild("ktsu-dev", "BuildMonitor", "dotnet.yml", RunStatus.Failure, "main", out BranchName branch);

		Filters.FilterOwner = "KTSU";
		Filters.FilterRepository = "^build";
		Filters.FilterRepositoryType = TextFilterType.Regex;
		Filters.FilterBuildName = "dtnt";
		Filters.FilterBuildNameType = TextFilterType.Fuzzy;
		Filters.FilterStatus = "fail";
		Assert.IsTrue(BuildMonitor.ShouldShowBuildBranch(build, branch));

		Filters.FilterStatus = "-failure";
		Assert.IsFalse(BuildMonitor.ShouldShowBuildBranch(build, branch));
	}

	[TestMethod]
	public void AnEmptyRepositoryIsFilteredByOwnerAndRepository()
	{
		Owner owner = new() { Name = "ktsu-dev".As<OwnerName>() };
		Repository repository = new() { Name = "BuildMonitor".As<RepositoryName>(), Owner = owner };

		Filters.FilterOwner = "kdv";
		Filters.FilterOwnerType = TextFilterType.Fuzzy;
		Filters.FilterRepository = "^build";
		Filters.FilterRepositoryType = TextFilterType.Regex;
		Assert.IsTrue(BuildMonitor.ShouldShowEmptyRepository(repository));

		Filters.FilterOwner = "-ktsu-dev";
		Filters.FilterOwnerType = TextFilterType.Glob;
		Assert.IsFalse(BuildMonitor.ShouldShowEmptyRepository(repository));
	}
}
