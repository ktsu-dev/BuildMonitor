// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.UITests.Gallery;

using System.Text;

using ktsu.ImGui.App.Testing;

/// <summary>One picture in the app gallery: how to stage it, and what to say about it.</summary>
/// <param name="Name">The caption, which also names the picture's file.</param>
/// <param name="Description">One or two sentences under the picture in the gallery's index.</param>
/// <param name="Stage">
/// Drives the application into the state worth photographing, starting from the seeded builds on
/// the All tab, settled. Popups and menus are opened the way a user opens them, with the pointer.
/// </param>
internal sealed record GalleryEntry(string Name, string Description, Action<ImGuiAppHarness> Stage)
{
	/// <summary>
	/// Gets the part of the window to keep, or null for all of it. Asked after <see cref="Stage"/>
	/// has run, so it can measure what the stage put on screen.
	/// </summary>
	public Func<ImGuiAppHarness, Rectangle?>? Crop { get; init; }

	/// <summary>Gets the file name the picture is written under, without its extension.</summary>
	public string Slug => MakeSlug(Name);

	/// <inheritdoc/>
	public override string ToString() => Name;

	/// <summary>Turns a caption into a lower-case, hyphenated file name.</summary>
	internal static string MakeSlug(string text)
	{
		StringBuilder slug = new(text.Length);
		foreach (char character in text)
		{
			if (char.IsAsciiLetterOrDigit(character))
			{
				slug.Append(char.ToLowerInvariant(character));
			}
			else if (slug.Length > 0 && slug[^1] != '-')
			{
				slug.Append('-');
			}
		}

		return slug.ToString().TrimEnd('-');
	}
}
