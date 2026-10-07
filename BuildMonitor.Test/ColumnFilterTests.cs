// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using ktsu.TextFilter;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the column filters for each filter type the search box offers. Every column used to wrap
/// the filter in <c>*…*</c> and uppercase it, which only suited a plain Glob word: Regex matched
/// everything, Fuzzy matched nothing, and Glob prefixes were ignored (ktsu-dev/BuildMonitor#319).
/// </summary>
[TestClass]
public sealed class ColumnFilterTests
{
	private const TextFilterMatchOptions WholeString = TextFilterMatchOptions.ByWholeString;

	[TestMethod]
	public void AnEmptyFilterShowsEverything() =>
		Assert.IsTrue(ColumnFilter.IsMatch("main", string.Empty, TextFilterType.Glob, WholeString));

	[TestMethod]
	public void APlainGlobWordMatchesAnywhereIgnoringCase()
	{
		Assert.IsTrue(ColumnFilter.IsMatch("main", "AI", TextFilterType.Glob, WholeString));
		Assert.IsTrue(ColumnFilter.IsMatch("feature/Login", "login", TextFilterType.Glob, WholeString));
		Assert.IsFalse(ColumnFilter.IsMatch("develop", "main", TextFilterType.Glob, WholeString));
	}

	[TestMethod]
	public void AnAnchoredRegexMatchesOnlyThatBranch()
	{
		Assert.IsTrue(ColumnFilter.IsMatch("main", "^main$", TextFilterType.Regex, WholeString));
		Assert.IsFalse(ColumnFilter.IsMatch("maintenance", "^main$", TextFilterType.Regex, WholeString));
		Assert.IsFalse(ColumnFilter.IsMatch("develop", "^main$", TextFilterType.Regex, WholeString));
	}

	[TestMethod]
	public void ARegexCharacterClassKeepsItsMeaning()
	{
		Assert.IsTrue(ColumnFilter.IsMatch("1234", @"^\d+$", TextFilterType.Regex, WholeString));
		Assert.IsFalse(ColumnFilter.IsMatch("main", @"^\d+$", TextFilterType.Regex, WholeString));
	}

	[TestMethod]
	public void ARegexIgnoresCase() =>
		Assert.IsTrue(ColumnFilter.IsMatch("Main", "^main$", TextFilterType.Regex, WholeString));

	[TestMethod]
	public void AFuzzyFilterMatchesCharactersInOrder()
	{
		Assert.IsTrue(ColumnFilter.IsMatch("main", "mn", TextFilterType.Fuzzy, WholeString));
		Assert.IsFalse(ColumnFilter.IsMatch("develop", "mn", TextFilterType.Fuzzy, WholeString));
	}

	[TestMethod]
	public void AGlobExclusionHidesTheBranchAndShowsTheRest()
	{
		Assert.IsFalse(ColumnFilter.IsMatch("main", "-main", TextFilterType.Glob, WholeString));
		Assert.IsTrue(ColumnFilter.IsMatch("develop", "-main", TextFilterType.Glob, WholeString));
	}

	[TestMethod]
	public void OnlyUnprefixedGlobWordsAreWrapped() =>
		Assert.AreEqual("*a* -b !c ^d +e *f*", ColumnFilter.MakeGlobMatchAnywhere("a -b  !c ^d +e f"));
}
