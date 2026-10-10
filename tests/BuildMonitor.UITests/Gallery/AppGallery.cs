// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.UITests.Gallery;

using System.Globalization;
using System.IO.Abstractions.TestingHelpers;

using ktsu.CredentialCache.Storage;
using ktsu.ImGui.App.Testing;

using CredentialCache = ktsu.CredentialCache.CredentialCache;
using StorageAppData = ktsu.AppDataStorage.AppData;

/// <summary>
/// Photographs the application for <c>docs/gallery</c>: one picture per <see cref="GalleryCatalog"/>
/// entry, and an index that captions them.
/// </summary>
/// <remarks>
/// <para>
/// These run as ordinary tests, so every pull request proves each picture can still be staged.
/// Pictures are written to a temporary directory unless <c>BUILDMONITOR_GALLERY_OUT</c> names one,
/// which is how the gallery workflow, and anyone regenerating by hand, sends them to
/// <c>docs/gallery</c>.
/// </para>
/// <para>
/// Each entry starts its own application from <see cref="GallerySeed"/>. The clock is stopped, the
/// time zone and culture are pinned, the providers never poll, tokens go to an in-memory store and
/// the app data file to an in-memory file system, so a picture is a function of the code alone and
/// taking one touches neither the network nor the machine's own settings and secrets.
/// </para>
/// </remarks>
[TestClass]
public sealed class AppGallery
{
	/// <summary>The environment variable naming the directory the gallery is written to.</summary>
	internal const string OutputVariable = "BUILDMONITOR_GALLERY_OUT";

	/// <summary>The display the gallery is drawn at: wide enough for every column of the build table.</summary>
	internal static readonly (int Width, int Height) Display = (1920, 640);

	private static readonly Lazy<string> TemporaryOutput = new(() =>
		Path.Combine(Path.GetTempPath(), $"buildmonitor-gallery-{Guid.NewGuid():N}"));

	/// <summary>Gets or sets the test context.</summary>
	public TestContext TestContext { get; set; } = null!;

	/// <summary>Gets every entry's name, one test case each.</summary>
	public static IEnumerable<object[]> EntryNames => GalleryCatalog.Entries.Select(entry => new object[] { entry.Name });

	/// <summary>Gets the directory pictures are written to.</summary>
	internal static string OutputDirectory =>
		Environment.GetEnvironmentVariable(OutputVariable) is string output && output.Length > 0
			? Path.GetFullPath(output)
			: TemporaryOutput.Value;

	/// <summary>Removes the temporary directory, when pictures went there rather than to a named one.</summary>
	[ClassCleanup]
	public static void DeleteTemporaryOutput()
	{
		if (TemporaryOutput.IsValueCreated && Directory.Exists(TemporaryOutput.Value))
		{
			Directory.Delete(TemporaryOutput.Value, recursive: true);
		}
	}

	[TestMethod]
	[DynamicData(nameof(EntryNames))]
	public void Photograph(string name)
	{
		GalleryEntry entry = GalleryCatalog.Entries.Single(candidate => candidate.Name == name);

		using GalleryEnvironment environment = new();
		using ImGuiAppHarness harness = environment.Start();
		Assert.IsTrue(GalleryFonts.Load(), "ImGuiApp's own font could not be found, so the pictures would not look like the application.");
		harness.Mouse.MoveTo(-100f, -100f);
		harness.Step(2);

		entry.Stage(harness);
		harness.Step(2);

		Bitmap32 frame = harness.Target;
		Rectangle region = entry.Crop?.Invoke(harness) ?? new Rectangle(0, 0, frame.Width, frame.Height);
		Bitmap32 picture = Crop(frame, TrimEmptyBottom(frame, region));

		Directory.CreateDirectory(OutputDirectory);
		string path = Path.Combine(OutputDirectory, entry.Slug + ".png");
		picture.SavePng(path);
		TestContext.WriteLine($"Wrote {path} ({picture.Width}x{picture.Height}).");
	}

	[TestMethod]
	public void WriteTheIndex()
	{
		string[] slugs = [.. GalleryCatalog.Entries.Select(entry => entry.Slug)];
		Assert.HasCount(slugs.Length, slugs.Distinct(StringComparer.Ordinal), "Two gallery entries would write the same file.");

		Directory.CreateDirectory(OutputDirectory);
		File.WriteAllText(Path.Combine(OutputDirectory, "README.md"), GalleryIndex.Render(GalleryCatalog.Entries));
	}

	/// <summary>
	/// Raises the bottom of a region to just below the lowest thing drawn in it, so a picture is not
	/// mostly the empty window under a short table.
	/// </summary>
	/// <remarks>
	/// A row counts as empty when every pixel inside the window's border matches the one in the
	/// middle of the region's last row, which is the window's background, or under a modal its
	/// dimmed background. The display is tall enough for the tallest popup, so measuring beats
	/// choosing one height that is too short for the context menu or too tall for the table.
	/// </remarks>
	internal static Rectangle TrimEmptyBottom(Bitmap32 source, Rectangle region)
	{
		const int Border = 2;
		const int Margin = 12;

		int minX = Math.Clamp(region.MinX + Border, 0, source.Width);
		int maxX = Math.Clamp(region.MaxX - Border, minX, source.Width);
		int minY = Math.Clamp(region.MinY, 0, source.Height);
		int maxY = Math.Clamp(region.MaxY, minY, source.Height);
		if (maxX <= minX || maxY - minY <= Border)
		{
			return region;
		}

		ReadOnlySpan<byte> pixels = source.Pixels;
		int sampleY = maxY - Border - 1;
		ReadOnlySpan<byte> background = pixels.Slice(((sampleY * source.Width) + ((minX + maxX) / 2)) * 4, 4);

		int lastDrawn = sampleY;
		while (lastDrawn > minY && IsEmptyRow(pixels, source.Width, lastDrawn, minX, maxX, background))
		{
			lastDrawn--;
		}

		int bottom = Math.Min(maxY, lastDrawn + 1 + Margin);
		return new Rectangle(region.MinX, region.MinY, region.MaxX, bottom);
	}

	private static bool IsEmptyRow(ReadOnlySpan<byte> pixels, int width, int y, int minX, int maxX, ReadOnlySpan<byte> background)
	{
		for (int x = minX; x < maxX; x++)
		{
			if (!pixels.Slice(((y * width) + x) * 4, 4).SequenceEqual(background))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>Copies a rectangle out of a frame, clamped to its edges.</summary>
	internal static Bitmap32 Crop(Bitmap32 source, Rectangle region)
	{
		int minX = Math.Clamp(region.MinX, 0, source.Width);
		int minY = Math.Clamp(region.MinY, 0, source.Height);
		int maxX = Math.Clamp(region.MaxX, minX, source.Width);
		int maxY = Math.Clamp(region.MaxY, minY, source.Height);
		Assert.IsTrue(maxX > minX && maxY > minY, $"The crop {region} leaves nothing of a {source.Width}x{source.Height} frame.");

		Bitmap32 cropped = new(maxX - minX, maxY - minY);
		int rowBytes = cropped.Width * 4;
		for (int y = minY; y < maxY; y++)
		{
			source.Pixels.Slice(((y * source.Width) + minX) * 4, rowBytes)
				.CopyTo(cropped.Pixels.Slice((y - minY) * rowBytes, rowBytes));
		}

		return cropped;
	}

	/// <summary>Everything the gallery pins or replaces for one picture, put back when disposed.</summary>
	private sealed class GalleryEnvironment : IDisposable
	{
		private readonly CultureInfo previousCulture = CultureInfo.CurrentCulture;
		private readonly CultureInfo previousUICulture = CultureInfo.CurrentUICulture;
		private readonly CredentialCache credentials = new(new InMemoryCredentialStore());
		private DateTimeOffset now = GallerySeed.Now;

		/// <summary>Starts the application on the seeded builds, with the clock stopped at <see cref="GallerySeed.Now"/>.</summary>
		/// <returns>The running application.</returns>
		internal ImGuiAppHarness Start()
		{
			// Percentages are formatted with the current culture, which differs by machine.
			CultureInfo culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
			culture.NumberFormat.PercentPositivePattern = 1;
			CultureInfo.CurrentCulture = culture;
			CultureInfo.CurrentUICulture = culture;

			// Every save the application makes goes to an in-memory file system for as long as this
			// factory is set, on whichever thread makes it.
			StorageAppData.ConfigureForTesting(() => new MockFileSystem());
			TokenStorage.UseCache(credentials);

			BuildMonitor.ResetState();
			BuildMonitor.PollProviders = false;
			Clock.LocalZoneOverride = TimeZoneInfo.Utc;
			Clock.UtcNowOverride = () => now;

			BuildMonitor.AppData = GallerySeed.Create();
			GallerySeed.FreezePollTimers(BuildMonitor.AppData);

			// Started a few minutes before the pictures are taken, so the log reads as a session.
			now = GallerySeed.Now.AddMinutes(-6);
			ImGuiAppHarness harness = ImGuiAppHarness.Start(BuildMonitor.BuildConfig(), new HarnessOptions
			{
				Width = Display.Width,
				Height = Display.Height,
				DpiScale = 1f,
			});
			WriteSessionLog();
			now = GallerySeed.Now;
			return harness;
		}

		public void Dispose()
		{
			BuildMonitor.ResetState();
			Clock.Reset();
			TokenStorage.UseCache(null);
			credentials.Dispose();
			StorageAppData.ResetFileSystem();
			CultureInfo.CurrentCulture = previousCulture;
			CultureInfo.CurrentUICulture = previousUICulture;
		}

		/// <summary>Writes what a session polling the seeded builds would have logged, a few seconds apart.</summary>
		private void WriteSessionLog()
		{
			Advance(1.204);
			Log.Info($"GitHub: Repository update for {GallerySeed.GitHubOwner}: 8 new, 0 updated, 0 archived removed, 8 total");
			Advance(0.318);
			Log.Info($"GitHub: Repository update for {GallerySeed.SecondGitHubOwner}: 1 new, 0 updated, 0 archived removed, 1 total");
			Advance(0.951);
			Log.Info($"AzureDevOps: Found 2 build definition(s) for 'Platform/Platform'");
			Advance(2.377);
			Log.Debug("UpdateBuilds: polling 10 builds");
			Advance(0.642);
			Log.Debug($"BuildSync: {GallerySeed.GitHubOwner}/ImGuiApp/dotnet.yml polled, 4 runs (no change)");
			Advance(58.03);
			Log.Info($"Build started: {GallerySeed.GitHubOwner}/BuildMonitor/dotnet.yml on main");
			Advance(36.5);
			Log.Debug("GitHub: Rate limit pacing - waiting 640ms before request 'GetWorkflowRuns'");
			Advance(48.19);
			Log.Warning($"Build failed: {GallerySeed.GitHubOwner}/Semantics/dotnet.yml on feature/vector-units");
			Advance(29.4);
			Log.Error("AzureDevOps: HttpRequestException for request 'GetBuildsAsync' - The SSL connection could not be established, see inner exception.");
			Advance(31.86);
			Log.Info($"Build started: {GallerySeed.GitHubOwner}/Keybinding/release.yml on main");
			Advance(12.07);
			Log.Info($"Build succeeded: {GallerySeed.AzureDevOpsAccount}/Platform/Platform-CI on main");
		}

		private void Advance(double seconds) => now = now.AddSeconds(seconds);
	}
}
