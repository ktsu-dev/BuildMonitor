// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.UITests.Gallery;

using ktsu.ImGui.App.Testing;

/// <summary>Finds the owner tabs in a frame by their pixels, and clicks one the way a user would.</summary>
/// <remarks>
/// <para>
/// The tabs come from <c>ktsu.ImGui.Widgets</c>' <c>TabPanel</c>, which records nothing for a probe, so
/// they are reached by geometry, as every vendor widget is in the ktsu UI suites. Setting
/// <c>AppData.SelectedOwnerTabId</c> instead does not work: <c>TabPanel</c> takes its active tab from
/// Dear ImGui rather than telling Dear ImGui which one to show, so the first frame drawn puts the
/// selection back on whatever tab ImGui already had open.
/// </para>
/// <para>
/// A tab is a run of colour on a row of the tab bar, separated from the next by a few pixels of window
/// background. The bar is the lowest row above the build table's filter boxes that splits into exactly
/// as many runs as there are tabs, and anything else fails the picture rather than clicking a guess.
/// </para>
/// </remarks>
internal static class OwnerTabs
{
	/// <summary>The tabs in the order the application draws them: All, the owners by label, then Logs.</summary>
	internal static IReadOnlyList<string> Labels { get; } =
		["All", GallerySeed.SecondGitHubOwner, GallerySeed.AzureDevOpsAccount, GallerySeed.GitHubOwner, "Logs"];

	/// <summary>The narrowest run of colour counted as a tab, which keeps text on a row from counting.</summary>
	private const int MinimumTabWidth = 20;

	/// <summary>The narrowest run of background counted as a gap between two tabs.</summary>
	private const int MinimumGap = 3;

	/// <summary>The filter box the table draws first, below the tab bar.</summary>
	private const string FirstFilter = "##FilterOwner";

	/// <summary>Clicks the tab with the given label and lets the application draw it.</summary>
	/// <param name="harness">The running application.</param>
	/// <param name="label">One of <see cref="Labels"/>.</param>
	internal static void Click(ImGuiAppHarness harness, string label)
	{
		int index = Labels.ToList().IndexOf(label);
		Assert.IsGreaterThanOrEqualTo(0, index, $"'{label}' is not one of the tabs the gallery knows about.");

		Rectangle filter = harness.Probe.Rect(FirstFilter)
			?? throw new InvalidOperationException("The build table was not drawn, so there is no tab bar to find.");
		Bitmap32 frame = harness.Target;
		for (int y = filter.MinY - 1; y >= 0; y--)
		{
			List<(int Start, int End)> tabs = RunsOnRow(frame, y);
			if (tabs.Count == Labels.Count)
			{
				(int start, int end) = tabs[index];
				harness.Mouse.Click((start + end) / 2f, y - 2f);
				harness.Step(3);
				return;
			}
		}

		Assert.Fail($"No row above the build table splits into the {Labels.Count} tabs {string.Join(", ", Labels)}.");
	}

	/// <summary>Splits a row into runs of anything but the window background, ignoring narrow ones.</summary>
	private static List<(int Start, int End)> RunsOnRow(Bitmap32 frame, int y)
	{
		ReadOnlySpan<byte> row = frame.Pixels.Slice(y * frame.Width * 4, frame.Width * 4);

		// The window's left margin is background on every row the tab bar can be on.
		ReadOnlySpan<byte> background = row.Slice(2 * 4, 4);

		List<(int Start, int End)> runs = [];
		int runStart = -1;
		int gap = MinimumGap;
		for (int x = 0; x < frame.Width; x++)
		{
			bool isBackground = row.Slice(x * 4, 4).SequenceEqual(background);
			if (isBackground)
			{
				gap++;
				if (gap == MinimumGap && runStart >= 0)
				{
					AddIfWideEnough(runs, runStart, x - MinimumGap);
					runStart = -1;
				}
			}
			else
			{
				if (runStart < 0 && gap >= MinimumGap)
				{
					runStart = x;
				}

				gap = 0;
			}
		}

		if (runStart >= 0)
		{
			AddIfWideEnough(runs, runStart, frame.Width - 1 - Math.Min(gap, MinimumGap));
		}

		return runs;
	}

	private static void AddIfWideEnough(List<(int Start, int End)> runs, int start, int end)
	{
		if (end - start + 1 >= MinimumTabWidth)
		{
			runs.Add((start, end));
		}
	}
}
