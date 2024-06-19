// Ignore Spelling: App

namespace ktsu.io.BuildMonitor;

using ktsu.io.AppDataStorage;
using ktsu.io.ImGuiApp;

internal class AppData : AppData<AppData>
{
	public ImGuiAppWindowState WindowState { get; set; } = new();

	public Dictionary<BuildProviderName, BuildProvider> BuildProviders { get; set; } = [];
}
