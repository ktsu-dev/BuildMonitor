namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;

using ktsu.AppDataStorage;
using ktsu.ImGuiApp;

internal class AppData : AppData<AppData>
{
	public ImGuiAppWindowState WindowState { get; set; } = new();

	public ConcurrentDictionary<BuildProviderName, BuildProvider> BuildProviders { get; set; } = [];
}
