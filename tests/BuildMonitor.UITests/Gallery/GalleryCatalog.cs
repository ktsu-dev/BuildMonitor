// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.UITests.Gallery;

using ktsu.ImGui.App.Testing;

/// <summary>Every picture in the app gallery, in the order the index shows them.</summary>
/// <remarks>
/// Each stage starts from <see cref="GallerySeed"/>'s builds on the All tab, settled. A new view
/// earns a picture by adding an entry here; nothing else needs to change, because the runner, the
/// index and the workflow all read this list.
/// </remarks>
internal static class GalleryCatalog
{
	/// <summary>The probe name of the failed run's error cell.</summary>
	private const string FailedRunErrors = "Errors/" + GallerySeed.FailedRunId;

	/// <summary>Gets the entries.</summary>
	internal static IReadOnlyList<GalleryEntry> Entries { get; } =
	[
		new(
			"All builds",
			"Every workflow the providers found, across GitHub owners and Azure DevOps projects, one row per build and branch with the most recently started first. Running builds show their progress against an estimate taken from recent successful runs, with an ETA; the history column shows the last five finished runs, and each row counts down to its next poll, sooner for builds that are running or have just failed.",
			_ => { }),
		new(
			"An owner's builds",
			"Each GitHub owner and each Azure DevOps organization has a tab of its own. Icons beside a repository's name mark it as private or a fork, and a repository with no workflows is still listed.",
			harness => SelectTab(harness, GallerySeed.GitHubOwner)),
		new(
			"Error details",
			"A failed run's errors are read from its job logs. Clicking them opens the whole list.",
			harness =>
			{
				harness.Click(FailedRunErrors);
				harness.Step(3);
				Park(harness);
			}),
		new(
			"Build actions",
			"Right-clicking a row opens links to the repository, the workflow, the branch and the latest run, and on GitHub can re-run, cancel or trigger the workflow.",
			harness =>
			{
				Rectangle cell = harness.Probe.Rect(FailedRunErrors)
					?? throw new InvalidOperationException("The failed run's errors were not drawn.");
				harness.Mouse.Click((cell.MinX + cell.MaxX) / 2f, (cell.MinY + cell.MaxY) / 2f, 1);
				harness.Step(3);
			}),
		new(
			"Logs",
			"What the monitor has been doing, colour-coded by level: discovery, run transitions, rate-limit pacing and anything that failed.",
			harness => SelectTab(harness, "Logs")),
	];

	/// <summary>Clicks a tab, then moves the pointer away so the tab is not drawn hovered.</summary>
	private static void SelectTab(ImGuiAppHarness harness, string label)
	{
		OwnerTabs.Click(harness, label);
		Park(harness);
	}

	/// <summary>Moves the pointer off the window, so nothing it was over is drawn hovered.</summary>
	private static void Park(ImGuiAppHarness harness)
	{
		harness.Mouse.MoveTo(-100f, -100f);
		harness.Step(2);
	}
}
