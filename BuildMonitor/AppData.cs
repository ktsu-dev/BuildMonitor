// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;

using ktsu.AppDataStorage;
using ktsu.ImGui.App;
using ktsu.TextFilter;

internal sealed class AppData : AppData<AppData>
{
	public ImGuiAppWindowState WindowState { get; set; } = new();

	public ConcurrentDictionary<BuildProviderName, BuildProvider> BuildProviders { get; set; } = [];

	public Dictionary<string, float> ColumnWidths { get; set; } = [];

	public string FilterOwner { get; set; } = string.Empty;
	public TextFilterType FilterOwnerType { get; set; } = TextFilterType.Glob;
	public TextFilterMatchOptions FilterOwnerMatchOptions { get; set; } = TextFilterMatchOptions.ByWordAny;

	public string FilterRepository { get; set; } = string.Empty;
	public TextFilterType FilterRepositoryType { get; set; } = TextFilterType.Glob;
	public TextFilterMatchOptions FilterRepositoryMatchOptions { get; set; } = TextFilterMatchOptions.ByWordAny;

	public string FilterBuildName { get; set; } = string.Empty;
	public TextFilterType FilterBuildNameType { get; set; } = TextFilterType.Glob;
	public TextFilterMatchOptions FilterBuildNameMatchOptions { get; set; } = TextFilterMatchOptions.ByWordAny;

	public string FilterStatus { get; set; } = string.Empty;
	public TextFilterType FilterStatusType { get; set; } = TextFilterType.Glob;
	public TextFilterMatchOptions FilterStatusMatchOptions { get; set; } = TextFilterMatchOptions.ByWordAny;

	public string FilterBranch { get; set; } = string.Empty;
	public TextFilterType FilterBranchType { get; set; } = TextFilterType.Glob;
	public TextFilterMatchOptions FilterBranchMatchOptions { get; set; } = TextFilterMatchOptions.ByWordAny;

	public string FilterOwnerTab { get; set; } = string.Empty;
	public TextFilterType FilterOwnerTabType { get; set; } = TextFilterType.Glob;
	public TextFilterMatchOptions FilterOwnerTabMatchOptions { get; set; } = TextFilterMatchOptions.ByWordAny;

	public string? SelectedOwnerTabId { get; set; }
}
