// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor;

using Hexa.NET.ImGui;

/// <summary>
/// Shows text in a tooltip exactly as given.
/// </summary>
/// <remarks>
/// <c>ImGui.SetTooltip(string)</c> takes its argument as a printf format and forwards it to native
/// <c>igSetTooltip</c> with no arguments. Build-log errors and provider exception messages routinely
/// contain <c>%s</c> or <c>%VAR%</c>, which made <c>vsnprintf</c> read arguments that were never
/// passed: garbage at best, a native access violation at worst (ktsu-dev/BuildMonitor#348). Text
/// that did not come from this code goes through here instead.
/// </remarks>
internal static class Tooltip
{
	/// <summary>
	/// Shows <paramref name="text"/> in a tooltip for the hovered item, without format processing.
	/// </summary>
	/// <param name="text">The text to show, which may contain any characters.</param>
	internal static void Show(string text)
	{
		ImGui.BeginTooltip();
		ImGui.TextUnformatted(text);
		ImGui.EndTooltip();
	}
}
