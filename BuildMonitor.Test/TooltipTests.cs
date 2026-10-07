// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using Hexa.NET.ImGui;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers tooltips built from text the app does not control. <c>ImGui.SetTooltip</c> treated that
/// text as a printf format, so an error line such as <c>format '%s' expects %d</c> was mangled or
/// crashed the process when hovered (ktsu-dev/BuildMonitor#348).
/// </summary>
/// <remarks>
/// The tooltip is rendered in a headless ImGui context and compared by vertex count: every visible
/// glyph is one quad, so two strings of the same length with no spaces draw the same number of
/// vertices only when every character of both was drawn. A format pass collapses <c>%%</c> to one
/// <c>%</c> and drops a glyph. A specifier such as <c>%s</c> would read a missing argument, which is
/// the crash this guards against, so the test uses the deterministic case.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class TooltipTests
{
	private static int RenderTooltipVertexCount(string text)
	{
		ImGuiContextPtr context = ImGui.CreateContext();
		try
		{
			ImGuiIOPtr io = ImGui.GetIO();
			io.DisplaySize = new System.Numerics.Vector2(800, 600);
			io.DeltaTime = 1f / 60f;
			io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;

			// A tooltip is sized on the frame it appears and drawn from the next, so render a few.
			int vertexCount = 0;
			for (int frame = 0; frame < 3; frame++)
			{
				ImGui.NewFrame();
				Tooltip.Show(text);
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

	[TestMethod]
	public void APercentSignIsDrawnLiterally()
	{
		int literal = RenderTooltipVertexCount("ab%%");
		int reference = RenderTooltipVertexCount("abcd");

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
}
