// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;

using ktsu.AppDataStorage;
using ktsu.ImGui.App;

internal sealed class AppData : AppData<AppData>
{
	public ImGuiAppWindowState WindowState { get; set; } = new();

	public ConcurrentDictionary<BuildProviderName, BuildProvider> BuildProviders { get; set; } = [];

	public Dictionary<string, float> ColumnWidths { get; set; } = [];
}
