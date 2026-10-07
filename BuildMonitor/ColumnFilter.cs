// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor;

using ktsu.TextFilter;

/// <summary>
/// Matches a table cell against the filter typed into its column header.
/// </summary>
/// <remarks>
/// Every column used to wrap the filter in <c>*…*</c> and uppercase both sides, whatever filter type
/// the user had picked. That is only right for a plain Glob word. A Regex such as <c>*MAIN*</c> does
/// not parse and matched everything, and uppercasing turned <c>\d</c> into <c>\D</c>. A Fuzzy filter
/// had to find the two literal asterisks and matched nothing. A Glob token such as <c>-main</c>
/// lost its prefix to the leading <c>*</c> (ktsu-dev/BuildMonitor#319).
/// </remarks>
internal static class ColumnFilter
{
	private static readonly char[] GlobPrefixes = ['-', '!', '^', '+'];

	/// <summary>
	/// Determines whether <paramref name="value"/> passes <paramref name="filter"/>, ignoring case.
	/// </summary>
	/// <param name="value">The cell text.</param>
	/// <param name="filter">The filter the user typed. An empty filter passes everything.</param>
	/// <param name="filterType">The filter type picked for the column.</param>
	/// <param name="matchOptions">The match options picked for the column.</param>
	/// <returns><see langword="true"/> if the cell should be shown.</returns>
	internal static bool IsMatch(string value, string filter, TextFilterType filterType, TextFilterMatchOptions matchOptions)
	{
		if (string.IsNullOrEmpty(filter))
		{
			return true;
		}

		string pattern = filterType == TextFilterType.Glob ? MakeGlobMatchAnywhere(filter) : filter;
		return TextFilter.IsMatch(value, pattern, filterType, matchOptions, TextFilterCaseSensitivity.CaseInsensitive);
	}

	/// <summary>
	/// Lets each plain Glob word match anywhere in the cell, so typing <c>mai</c> finds <c>main</c>.
	/// A word that starts with a prefix character is left alone so the prefix keeps its meaning.
	/// </summary>
	/// <param name="filter">The Glob filter as typed.</param>
	/// <returns>The filter with each unprefixed word wrapped in <c>*…*</c>.</returns>
	internal static string MakeGlobMatchAnywhere(string filter)
	{
		IEnumerable<string> tokens = filter
			.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(token => GlobPrefixes.Contains(token[0]) ? token : $"*{token}*");
		return string.Join(' ', tokens);
	}
}
