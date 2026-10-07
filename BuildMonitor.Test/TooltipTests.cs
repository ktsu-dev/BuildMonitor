// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using System.Numerics;

using Hexa.NET.ImGui;

using ktsu.Semantics.Strings;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers tooltips built from text the app does not control. <c>ImGui.SetTooltip</c> treated that
/// text as a printf format, so an error line such as <c>format '%s' expects %d</c> was mangled or
/// crashed the process when hovered (ktsu-dev/BuildMonitor#348).
/// </summary>
/// <remarks>
/// Each case renders in a headless ImGui context and is compared by vertex count: every visible
/// glyph is one quad, so two strings of the same length with no spaces draw the same number of
/// vertices only when every character of both was drawn. A format pass collapses <c>%%</c> to one
/// <c>%</c> and drops a glyph. A specifier such as <c>%s</c> would read a missing argument, which is
/// the crash this guards against, so the tests use the deterministic case.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class TooltipTests
{
	private const string Literal = "ab%%";
	private const string Reference = "abcd";

	/// <summary>
	/// A point inside the first item of the test window, which is where the mouse rests so that
	/// item's hover tooltip opens.
	/// </summary>
	private static readonly Vector2 OverFirstItem = new(12, 12);

	/// <summary>
	/// Off every item, so nothing is hovered.
	/// </summary>
	private static readonly Vector2 OffEveryItem = new(-100, -100);

	private static int RenderVertexCount(Action draw, Vector2 mousePosition)
	{
		ImGuiContextPtr context = ImGui.CreateContext();
		try
		{
			ImGuiIOPtr io = ImGui.GetIO();
			io.DisplaySize = new Vector2(800, 600);
			io.DeltaTime = 1f / 60f;
			io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;

			// Hover and tooltips are resolved a frame after the mouse arrives, so render a few.
			int vertexCount = 0;
			for (int frame = 0; frame < 3; frame++)
			{
				io.MousePos = mousePosition;
				ImGui.NewFrame();
				ImGui.SetNextWindowPos(Vector2.Zero);
				ImGui.SetNextWindowSize(new Vector2(400, 200));
				ImGui.Begin("TooltipTests", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings);
				draw();
				ImGui.End();
				ImGui.Render();

				ImDrawDataPtr drawData = ImGui.GetDrawData();
				vertexCount = 0;
				for (int i = 0; i < drawData.CmdListsCount; i++)
				{
					vertexCount += drawData.CmdLists[i].VtxBuffer.Size;
				}
			}

			return vertexCount;
		}
		finally
		{
			ImGui.DestroyContext(context);
		}
	}

	private static int RenderTooltipVertexCount(string text) =>
		RenderVertexCount(() => Tooltip.Show(text), OffEveryItem);

	private static int RenderErrorsCellVertexCount(string error, Vector2 mousePosition)
	{
		Build build = new();
		Run run = new() { Build = build, Errors = [error] };
		return RenderVertexCount(() => BuildMonitor.RenderErrorsColumn(run, build, run.Branch), mousePosition);
	}

	private static int RenderProviderStatusVertexCount(string projectName, Vector2 mousePosition)
	{
		AzureDevOps provider = new();
		provider.ReportProjectNotFound(projectName.As<OwnerName>());
		return RenderVertexCount(() => BuildMonitor.RenderProviderStatus(provider), mousePosition);
	}

	[TestMethod]
	public void APercentSignIsDrawnLiterally()
	{
		int literal = RenderTooltipVertexCount(Literal);
		int reference = RenderTooltipVertexCount(Reference);

		Assert.IsGreaterThan(0, reference, "The reference tooltip should have drawn something");
		Assert.AreEqual(reference, literal, "Every character of \"ab%%\" should be drawn, as it is for \"abcd\"");
	}

	[TestMethod]
	public void ADoubledPercentAroundAWordDrawsEveryCharacter()
	{
		int literal = RenderTooltipVertexCount("'%%SIGNTOOL%%'");
		int reference = RenderTooltipVertexCount("'xxSIGNTOOLxx'");

		Assert.AreEqual(reference, literal);
	}

	[TestMethod]
	public void HoveringAnErrorsCellShowsTheErrorLiterally()
	{
		int unhovered = RenderErrorsCellVertexCount(Reference, OffEveryItem);
		int reference = RenderErrorsCellVertexCount(Reference, OverFirstItem);
		int literal = RenderErrorsCellVertexCount(Literal, OverFirstItem);

		Assert.IsGreaterThan(unhovered, reference, "Hovering the cell should open its tooltip");
		Assert.AreEqual(reference, literal, "The tooltip should draw every character of the error");
	}

	[TestMethod]
	public void HoveringAProviderStatusShowsItsMessageLiterally()
	{
		// The status text follows a small colour indicator, so rest the mouse further along the line.
		Vector2 overStatusText = new(80, 12);
		int unhovered = RenderProviderStatusVertexCount(Reference, OffEveryItem);
		int reference = RenderProviderStatusVertexCount(Reference, overStatusText);
		int literal = RenderProviderStatusVertexCount(Literal, overStatusText);

		Assert.IsGreaterThan(unhovered, reference, "Hovering the status should open its tooltip");
		Assert.AreEqual(reference, literal, "The tooltip should draw every character of the status message");
	}
}
