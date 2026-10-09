// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.UITests.Gallery;

/// <summary>Compares names ordinally and hashes them the same way in every process.</summary>
/// <remarks>
/// .NET randomizes string hashes per process, and a <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>
/// enumerates in hash order, so a dictionary keyed by name lists its entries in a different order on
/// every run. The provider status bar is drawn in that order. A fixed hash makes the order a
/// function of the names alone, which is what a picture compared byte for byte needs.
/// </remarks>
/// <typeparam name="TName">The name type, compared by its text.</typeparam>
internal sealed class StableNameComparer<TName> : IEqualityComparer<TName>
	where TName : class
{
	/// <summary>Gets the comparer.</summary>
	internal static StableNameComparer<TName> Instance { get; } = new();

	/// <inheritdoc/>
	public bool Equals(TName? x, TName? y) => string.Equals(x?.ToString(), y?.ToString(), StringComparison.Ordinal);

	/// <inheritdoc/>
	public int GetHashCode(TName obj)
	{
		// FNV-1a over the UTF-16 code units.
		uint hash = 2166136261;
		foreach (char character in obj.ToString() ?? string.Empty)
		{
			hash = (hash ^ character) * 16777619;
		}

		return unchecked((int)hash);
	}
}
