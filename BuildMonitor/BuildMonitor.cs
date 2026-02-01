// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using ktsu.Extensions;
using ktsu.ImGui.App;
using ktsu.ImGui.Styler;
using ktsu.ImGui.Widgets;
using ktsu.ImGui.Popups;
using ktsu.TextFilter;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling", Justification = "Main application class orchestrates many components")]
internal static class BuildMonitor
{
	internal static AppData AppData { get; set; } = new();
	private static bool ShouldSaveAppData { get; set; }

	private static Stopwatch ProviderRefreshTimer { get; } = new Stopwatch();

	private static int ProviderRefreshTimeout { get; set; } = 300;

	internal static object SyncLock { get; } = new();

	private static ConcurrentDictionary<BuildId, BuildSync> BuildSyncCollection { get; } = [];
	internal static ConcurrentDictionary<RunId, RunSync> RunSyncCollection { get; } = [];

	private static Task UpdateTask { get; set; } = new(() => { });

	internal static ConcurrentDictionary<string, DateTimeOffset> ActiveRequests { get; set; } = [];

	private static void Main()
	{
		AppData = AppData.LoadOrCreate();
		ImGuiApp.Start(new ImGuiAppConfig
		{
			Title = Strings.ApplicationName,
			InitialWindowState = AppData.WindowState,
			OnStart = OnStart,
			OnRender = OnRender,
			OnAppMenu = OnAppMenu,
			OnMoveOrResize = OnMoveOrResize
		});
	}

	private static void OnMoveOrResize()
	{
		AppData.WindowState = ImGuiApp.WindowState;
		QueueSaveAppData();
	}

	private static void OnStart()
	{
		Log.Info("Build Monitor starting...");

		bool needsSave = false;

		needsSave |= AppData.BuildProviders.TryAdd(GitHub.BuildProviderName, new GitHub());
		needsSave |= AppData.BuildProviders.TryAdd(AzureDevOps.BuildProviderName, new AzureDevOps());

		// add more providers here as needed

		if (needsSave)
		{
			QueueSaveAppData();
		}

		Log.Info($"Initialized {AppData.BuildProviders.Count} build providers");
		UpdateTask = UpdateAsync();
	}

	private static readonly Dictionary<string, float> DefaultColumnWidths = new()
	{
		["##buildStatus"] = 30f,
		[Strings.Owner] = 150f,
		[Strings.Repository] = 150f,
		[Strings.BuildName] = 200f,
		[Strings.Branch] = 150f,
		[Strings.Status] = 80f,
		[Strings.LastRun] = 180f,
		[Strings.Duration] = 80f,
		[Strings.Estimate] = 80f,
		[Strings.History] = 80f,
		[Strings.Progress] = 100f,
		[Strings.ETA] = 80f,
		[Strings.Errors] = 200f,
	};

	private static ImGuiPopups.Prompt ErrorDetailsPopup { get; } = new();
	private static string CurrentErrorDetails { get; set; } = string.Empty;

	private static ImGuiWidgets.TabPanel OwnerTabPanel { get; } = new("OwnerTabPanel", false, true, OnOwnerTabChanged);
	private static HashSet<string> CurrentOwnerTabIds { get; } = [];
	private const string AllOwnersTabId = "__all__";
	private const string LogsTabId = "__logs__";

	private static void OnOwnerTabChanged(string tabId)
	{
		AppData.SelectedOwnerTabId = tabId;
		QueueSaveAppData();
	}

	private static float GetColumnWidth(string columnName)
	{
		if (AppData.ColumnWidths.TryGetValue(columnName, out float width) &&
			width >= 1f && width <= 10000f && float.IsFinite(width))
		{
			return width;
		}

		return DefaultColumnWidths.GetValueOrDefault(columnName, 80f);
	}

	// TODO: Remove this workaround once Hexa.NET.ImGui fixes the ImGuiTableColumn struct layout.
	// The binding incorrectly uses sbyte/byte for ImGuiTableColumnIdx/ImGuiTableDrawChannelIdx fields
	// which should be short/ushort (2 bytes each). This makes the C# struct 8 bytes smaller than native.
	// See: https://github.com/HexaEngine/Hexa.NET.ImGui/issues/XXX (report this issue)
	//
	// 8 fields are wrong: DisplayOrder, IndexWithinEnabledSet, PrevEnabledColumn, NextEnabledColumn,
	// SortOrder (all ImGuiTableColumnIdx = ImS16), and DrawChannelCurrent, DrawChannelFrozen,
	// DrawChannelUnfrozen (all ImGuiTableDrawChannelIdx = ImU16). Each should be 2 bytes but is 1 byte.
	private const int ImGuiTableColumnSizeDifference = 8;

	private static unsafe int GetNativeImGuiTableColumnSize()
	{
		int csharpSize = sizeof(ImGuiTableColumn);
		int nativeSize = csharpSize + ImGuiTableColumnSizeDifference;

		// Native struct should be ~112 bytes according to imgui comments
		// If C# size >= 112, the fix has likely been applied
		Debug.Assert(
			csharpSize < 112,
			$"ImGuiTableColumn C# struct size is {csharpSize} bytes (expected < 112). " +
			"Check if Hexa.NET.ImGui fixed the struct layout and remove this workaround if so.");

		return nativeSize;
	}

	private static unsafe void SaveColumnWidth(string columnName, int columnIndex)
	{
		// Assert that WidthGiven is still at the expected offset (after Flags which is 4 bytes)
		Debug.Assert(
			Marshal.OffsetOf<ImGuiTableColumn>(nameof(ImGuiTableColumn.WidthGiven)).ToInt32() == 4,
			"ImGuiTableColumn.WidthGiven offset changed. Update the workaround.");

		ImGuiTablePtr table = ImGuiP.GetCurrentTable();
		if (table.Handle == null || columnIndex < 0 || columnIndex >= table.Handle->ColumnsCount)
		{
			return;
		}

		// Use manual pointer arithmetic with the correct native struct size
		// instead of relying on C# sizeof(ImGuiTableColumn) which is incorrect
		int nativeStructSize = GetNativeImGuiTableColumnSize();
		byte* basePtr = (byte*)table.Handle->Columns.Data;
		byte* columnAddress = basePtr + (columnIndex * nativeStructSize);

		// Read WidthGiven directly from offset 4 (after Flags which is 4 bytes)
		const int widthGivenOffset = 4;
		float currentWidth = *(float*)(columnAddress + widthGivenOffset);

		if (currentWidth < 1f || currentWidth > 10000f || !float.IsFinite(currentWidth))
		{
			return;
		}

		if (!AppData.ColumnWidths.TryGetValue(columnName, out float savedWidth) ||
			Math.Abs(savedWidth - currentWidth) > 1f)
		{
			AppData.ColumnWidths[columnName] = currentWidth;
			QueueSaveAppData();
		}
	}

	private static void RenderFilterSearchBox(
		string id,
		ref string filterText,
		ref TextFilterType filterType,
		ref TextFilterMatchOptions filterMatchOptions)
	{
		if (ImGui.TableNextColumn())
		{
			ImGui.SetNextItemWidth(-1);
			string text = filterText;
			TextFilterType type = filterType;
			TextFilterMatchOptions matchOptions = filterMatchOptions;
			ImGuiWidgets.SearchBox(id, ref text, ref type, ref matchOptions);
			if (text != filterText || type != filterType || matchOptions != filterMatchOptions)
			{
				filterText = text;
				filterType = type;
				filterMatchOptions = matchOptions;
				QueueSaveAppData();
			}
		}
	}

	private static void RenderFilterRow()
	{
		ImGui.TableNextRow();
		ImGui.TableNextColumn(); // Skip status column

		string filterOwner = AppData.FilterOwner;
		TextFilterType filterOwnerType = AppData.FilterOwnerType;
		TextFilterMatchOptions filterOwnerMatchOptions = AppData.FilterOwnerMatchOptions;
		RenderFilterSearchBox("##FilterOwner", ref filterOwner, ref filterOwnerType, ref filterOwnerMatchOptions);
		AppData.FilterOwner = filterOwner;
		AppData.FilterOwnerType = filterOwnerType;
		AppData.FilterOwnerMatchOptions = filterOwnerMatchOptions;

		string filterRepository = AppData.FilterRepository;
		TextFilterType filterRepositoryType = AppData.FilterRepositoryType;
		TextFilterMatchOptions filterRepositoryMatchOptions = AppData.FilterRepositoryMatchOptions;
		RenderFilterSearchBox("##FilterRepository", ref filterRepository, ref filterRepositoryType, ref filterRepositoryMatchOptions);
		AppData.FilterRepository = filterRepository;
		AppData.FilterRepositoryType = filterRepositoryType;
		AppData.FilterRepositoryMatchOptions = filterRepositoryMatchOptions;

		string filterBuildName = AppData.FilterBuildName;
		TextFilterType filterBuildNameType = AppData.FilterBuildNameType;
		TextFilterMatchOptions filterBuildNameMatchOptions = AppData.FilterBuildNameMatchOptions;
		RenderFilterSearchBox("##FilterBuildName", ref filterBuildName, ref filterBuildNameType, ref filterBuildNameMatchOptions);
		AppData.FilterBuildName = filterBuildName;
		AppData.FilterBuildNameType = filterBuildNameType;
		AppData.FilterBuildNameMatchOptions = filterBuildNameMatchOptions;

		string filterBranch = AppData.FilterBranch;
		TextFilterType filterBranchType = AppData.FilterBranchType;
		TextFilterMatchOptions filterBranchMatchOptions = AppData.FilterBranchMatchOptions;
		RenderFilterSearchBox("##FilterBranch", ref filterBranch, ref filterBranchType, ref filterBranchMatchOptions);
		AppData.FilterBranch = filterBranch;
		AppData.FilterBranchType = filterBranchType;
		AppData.FilterBranchMatchOptions = filterBranchMatchOptions;

		string filterStatus = AppData.FilterStatus;
		TextFilterType filterStatusType = AppData.FilterStatusType;
		TextFilterMatchOptions filterStatusMatchOptions = AppData.FilterStatusMatchOptions;
		RenderFilterSearchBox("##FilterStatus", ref filterStatus, ref filterStatusType, ref filterStatusMatchOptions);
		AppData.FilterStatus = filterStatus;
		AppData.FilterStatusType = filterStatusType;
		AppData.FilterStatusMatchOptions = filterStatusMatchOptions;
	}

	private static void OnRender(float dt)
	{
		if (UpdateTask.IsCompleted)
		{
			if (UpdateTask.IsFaulted && UpdateTask.Exception is not null)
			{
				throw UpdateTask.Exception;
			}

			UpdateTask = UpdateAsync();
		}

		foreach ((BuildProviderName? _, BuildProvider? buildProvider) in AppData.BuildProviders)
		{
			buildProvider.Tick();
		}

		RenderProviderStatusBar();
		UpdateOwnerTabs();
		OwnerTabPanel.Draw();

		ShowPopupsIfRequired();
		SaveSettingsIfRequired();
	}

	private static void UpdateOwnerTabs()
	{
		// Build expected tabs based on provider type:
		// - GitHub: one tab per owner (org/user)
		// - Azure DevOps: one tab per account (the provider itself)
		// - Always include Logs tab
		HashSet<string> expectedTabIds = [AllOwnersTabId, LogsTabId];
		Dictionary<string, (string Label, Action Content)> tabsToCreate = [];

		foreach ((BuildProviderName providerName, BuildProvider provider) in AppData.BuildProviders)
		{
			if (provider is GitHub)
			{
				// GitHub: create tabs per owner
				foreach ((OwnerName ownerName, Owner owner) in provider.Owners)
				{
					string tabId = $"GitHub:{ownerName}";
					expectedTabIds.Add(tabId);
					if (!CurrentOwnerTabIds.Contains(tabId))
					{
						Owner capturedOwner = owner;
						tabsToCreate[tabId] = (ownerName.ToString(), () => RenderOwnerTab(capturedOwner));
					}
				}
			}
			else if (provider is AzureDevOps adoProvider)
			{
				// Azure DevOps: create one tab per account
				string accountId = adoProvider.AccountId;
				if (!string.IsNullOrEmpty(accountId) && !adoProvider.Owners.IsEmpty)
				{
					string tabId = $"ADO:{accountId}";
					expectedTabIds.Add(tabId);
					if (!CurrentOwnerTabIds.Contains(tabId))
					{
						BuildProvider capturedProvider = provider;
						tabsToCreate[tabId] = (accountId, () => RenderProviderTab(capturedProvider));
					}
				}
			}
		}

		// Add "All" tab if not present
		if (!CurrentOwnerTabIds.Contains(AllOwnersTabId))
		{
			_ = OwnerTabPanel.AddTab(AllOwnersTabId, Strings.All, RenderAllOwnersTab);
			_ = CurrentOwnerTabIds.Add(AllOwnersTabId);
		}

		// Add new tabs (sorted by label for consistent ordering)
		foreach ((string tabId, (string label, Action content)) in tabsToCreate.OrderBy(t => t.Value.Label))
		{
			_ = OwnerTabPanel.AddTab(tabId, label, content);
			_ = CurrentOwnerTabIds.Add(tabId);
		}

		// Add "Logs" tab if not present (add at the end)
		if (!CurrentOwnerTabIds.Contains(LogsTabId))
		{
			_ = OwnerTabPanel.AddTab(LogsTabId, Strings.Logs, RenderLogsTab);
			_ = CurrentOwnerTabIds.Add(LogsTabId);
		}

		// Remove tabs that no longer exist
		List<string> tabsToRemove = [.. CurrentOwnerTabIds.Where(id => !expectedTabIds.Contains(id))];
		foreach (string tabId in tabsToRemove)
		{
			_ = OwnerTabPanel.RemoveTab(tabId);
			_ = CurrentOwnerTabIds.Remove(tabId);
		}

		// Restore selected tab if set
		if (AppData.SelectedOwnerTabId != null &&
			CurrentOwnerTabIds.Contains(AppData.SelectedOwnerTabId) &&
			OwnerTabPanel.ActiveTabId != AppData.SelectedOwnerTabId)
		{
			int tabIndex = OwnerTabPanel.GetTabIndex(AppData.SelectedOwnerTabId);
			if (tabIndex >= 0)
			{
				OwnerTabPanel.ActiveTabIndex = tabIndex;
			}
		}
	}

	private static void RenderAllOwnersTab() => RenderBuildTable(null, null);

	private static void RenderOwnerTab(Owner owner) => RenderBuildTable(owner, null);

	private static void RenderProviderTab(BuildProvider provider) => RenderBuildTable(null, provider);

	private static void RenderLogsTab()
	{
		if (ImGui.Button(Strings.ClearLogs))
		{
			Log.Clear();
		}

		ImGui.Separator();

		// Create a scrollable region for logs
		if (ImGui.BeginChild("LogsScrollRegion", new System.Numerics.Vector2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
		{
			foreach (LogEntry entry in Log.GetEntries())
			{
				ImColor color = entry.Level switch
				{
					LogLevel.Debug => Color.Palette.Neutral.Gray,
					LogLevel.Info => Color.Palette.Neutral.White,
					LogLevel.Warning => Color.Palette.Basic.Yellow,
					LogLevel.Error => Color.Palette.Basic.Red,
					_ => Color.Palette.Neutral.White,
				};

				using (new ScopedTextColor(color))
				{
					string timestamp = entry.Timestamp.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
					string levelStr = entry.Level.ToString().ToUpperInvariant();
					ImGui.TextUnformatted($"[{timestamp}] [{levelStr}] {entry.Message}");
				}
			}

			// Auto-scroll to bottom if we're already at the bottom
			if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 20)
			{
				ImGui.SetScrollHereY(1.0f);
			}
		}
		ImGui.EndChild();
	}

	private static void RenderBuildTable(Owner? filterOwner, BuildProvider? filterProvider)
	{
		if (ImGui.BeginTable(Strings.Builds, 13, ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg))
		{
			foreach (string columnName in DefaultColumnWidths.Keys)
			{
				ImGui.TableSetupColumn(columnName, ImGuiTableColumnFlags.WidthFixed, GetColumnWidth(columnName));
			}

			ImGui.TableHeadersRow();

			RenderFilterRow();

			// Group runs by (Build, Branch) and iterate
			IEnumerable<(BuildProviderName Name, BuildProvider Provider)> providers = filterProvider != null
				? [(filterProvider.Name, filterProvider)]
				: AppData.BuildProviders.Select(p => (p.Key, p.Value));

			IEnumerable<(Owner Owner, BuildProvider Provider)> owners = filterOwner != null
				? [(filterOwner, filterOwner.BuildProvider)]
				: providers.SelectMany(p => p.Provider.Owners.Select(o => (o.Value, p.Provider)));

			// Collect all repositories for tracking which ones have builds/runs
			List<Repository> allRepositories = [.. owners
				.SelectMany(o => o.Owner.Repositories.Values)];

			HashSet<RepositoryId> repositoriesWithRows = [];

			IOrderedEnumerable<(Build Build, BranchName Branch, List<Run> Runs)> buildBranches = owners
				.SelectMany(o => o.Owner.Repositories)
				.SelectMany(r => r.Value.Builds)
				.SelectMany(b => b.Value.Runs.Values
					.GroupBy(r => r.Branch)
					.Select(g => (Build: b.Value, Branch: g.Key, Runs: g.OrderByDescending(r => r.Started).ToList())))
				.OrderByDescending(x => x.Runs.FirstOrDefault()?.Started ?? DateTimeOffset.MinValue);

			foreach ((Build build, BranchName branch, List<Run> branchRuns) in buildBranches)
			{
				RenderBuildBranchRow(build, branch, branchRuns);
				_ = repositoriesWithRows.Add(build.Repository.Id);
			}

			// Show repositories that have no workflows/builds
			foreach (Repository repository in allRepositories.Where(r => !repositoriesWithRows.Contains(r.Id)))
			{
				RenderEmptyRepositoryRow(repository);
			}

			int saveColIndex = 0;
			foreach (string columnName in DefaultColumnWidths.Keys)
			{
				SaveColumnWidth(columnName, saveColIndex);
				saveColIndex++;
			}

			ImGui.EndTable();
		}
	}

	private static string MakeDurationFormat(TimeSpan duration)
	{
		string format = @"hh\:mm\:ss";
		if (duration.Days > 0)
		{
			format = @"d\.hh\:mm\:ss";
		}

		return format;
	}

	private static string MakeBuildDisplayName(Build build)
	{
		string displayName = build.Name;
		if (displayName.EndsWithOrdinal(".yml"))
		{
			displayName = displayName.RemoveSuffix(".yml");
		}

		if (displayName.StartsWithOrdinal(".github/workflows/"))
		{
			displayName = displayName.RemovePrefix(".github/workflows/");
		}

		return displayName;
	}

	private static string MakeRepositoryDisplayName(Build build)
	{
		string repoName = build.Repository.Name;
		string ownerName = build.Owner.Name;

		// Remove owner name prefix if present (e.g., "ktsu-dev-" from "ktsu-dev-BuildMonitor")
		if (repoName.StartsWithOrdinal(ownerName + "-"))
		{
			repoName = repoName.RemovePrefix(ownerName + "-");
		}
		else if (repoName.StartsWithOrdinal(ownerName + "."))
		{
			repoName = repoName.RemovePrefix(ownerName + ".");
		}
		else if (repoName.StartsWithOrdinal(ownerName + "_"))
		{
			repoName = repoName.RemovePrefix(ownerName + "_");
		}

		return repoName;
	}

	private static bool ShouldShowBuildBranch(Build build, BranchName branch)
	{
		bool shouldShow = true;
		if (!string.IsNullOrEmpty(AppData.FilterOwner))
		{
			shouldShow &= TextFilter.IsMatch(build.Owner.Name.ToString().ToUpperInvariant(), "*" + AppData.FilterOwner.ToUpperInvariant() + "*", AppData.FilterOwnerType, AppData.FilterOwnerMatchOptions);
		}

		if (!string.IsNullOrEmpty(AppData.FilterRepository))
		{
			string displayRepository = MakeRepositoryDisplayName(build);
			shouldShow &= TextFilter.IsMatch(displayRepository.ToUpperInvariant(), "*" + AppData.FilterRepository.ToUpperInvariant() + "*", AppData.FilterRepositoryType, AppData.FilterRepositoryMatchOptions);
		}

		if (!string.IsNullOrEmpty(AppData.FilterBuildName))
		{
			string displayName = MakeBuildDisplayName(build);
			shouldShow &= TextFilter.IsMatch(displayName.ToUpperInvariant(), "*" + AppData.FilterBuildName.ToUpperInvariant() + "*", AppData.FilterBuildNameType, AppData.FilterBuildNameMatchOptions);
		}

		if (!string.IsNullOrEmpty(AppData.FilterBranch))
		{
			shouldShow &= TextFilter.IsMatch(branch.ToString().ToUpperInvariant(), "*" + AppData.FilterBranch.ToUpperInvariant() + "*", AppData.FilterBranchType, AppData.FilterBranchMatchOptions);
		}

		if (!string.IsNullOrEmpty(AppData.FilterStatus))
		{
			Run? latestRun = build.Runs.Values
				.Where(r => r.Branch == branch)
				.OrderByDescending(r => r.Started)
				.FirstOrDefault();
			if (latestRun is not null)
			{
				shouldShow &= TextFilter.IsMatch(latestRun.Status.ToString().ToUpperInvariant(), "*" + AppData.FilterStatus.ToUpperInvariant() + "*", AppData.FilterStatusType, AppData.FilterStatusMatchOptions);
			}
		}

		return shouldShow;
	}

	private static void ShowBranchHistory(List<Run> branchRuns)
	{
		const int recentRuns = 5;

		List<Run> runs = [.. branchRuns
			.Where(r => r.Status != RunStatus.Canceled)
			.Take(recentRuns)];

		runs.Reverse();

		foreach (Run? run in runs)
		{
			ImGui.SameLine();
			ImGuiWidgets.ColorIndicator(GetStatusColor(run.Status), true);
		}
	}

	private static void RenderBuildBranchRow(Build build, BranchName branch, List<Run> branchRuns)
	{
		if (!ShouldShowBuildBranch(build, branch))
		{
			return;
		}

		Run? latestRun = branchRuns.FirstOrDefault();
		if (latestRun is null)
		{
			return;
		}

		bool shouldOpenContextMenu = RenderBuildBranchRowColumns(build, branch, branchRuns, latestRun);
		RenderBuildBranchContextMenu(build, branch, latestRun, shouldOpenContextMenu);
	}

	private static void RenderEmptyRepositoryRow(Repository repository)
	{
		if (!ShouldShowEmptyRepository(repository))
		{
			return;
		}

		ImGui.TableNextRow();

		// Status column - gray indicator for no builds
		if (ImGui.TableNextColumn())
		{
			ImGuiWidgets.ColorIndicator(Color.Palette.Neutral.Gray, true);
		}

		// Owner column
		_ = RenderTextColumn(repository.Owner.Name);

		// Repository column
		_ = RenderTextColumn(MakeRepositoryDisplayName(repository));

		// Build Name column - show "No workflows" in gray
		if (ImGui.TableNextColumn())
		{
			using (new ScopedTextColor(Color.Palette.Neutral.Gray))
			{
				ImGui.TextUnformatted(Strings.NoWorkflows);
			}
		}

		// Remaining columns - empty
		for (int i = 0; i < 9; i++)
		{
			if (ImGui.TableNextColumn())
			{
				ImGui.Dummy(new(1, 1));
			}
		}
	}

	private static bool ShouldShowEmptyRepository(Repository repository)
	{
		bool shouldShow = true;

		if (!string.IsNullOrEmpty(AppData.FilterOwner))
		{
			shouldShow &= TextFilter.IsMatch(repository.Owner.Name.ToString().ToUpperInvariant(), "*" + AppData.FilterOwner.ToUpperInvariant() + "*", AppData.FilterOwnerType, AppData.FilterOwnerMatchOptions);
		}

		if (!string.IsNullOrEmpty(AppData.FilterRepository))
		{
			string displayRepository = MakeRepositoryDisplayName(repository);
			shouldShow &= TextFilter.IsMatch(displayRepository.ToUpperInvariant(), "*" + AppData.FilterRepository.ToUpperInvariant() + "*", AppData.FilterRepositoryType, AppData.FilterRepositoryMatchOptions);
		}

		// If there's a build name, branch, or status filter, hide empty repositories
		// (they can't match these filters since they have no builds/runs)
		if (!string.IsNullOrEmpty(AppData.FilterBuildName) ||
			!string.IsNullOrEmpty(AppData.FilterBranch) ||
			!string.IsNullOrEmpty(AppData.FilterStatus))
		{
			return false;
		}

		return shouldShow;
	}

	private static string MakeRepositoryDisplayName(Repository repository)
	{
		string repoName = repository.Name;
		string ownerName = repository.Owner.Name;

		// Remove owner name prefix if present (e.g., "ktsu-dev-" from "ktsu-dev-BuildMonitor")
		if (repoName.StartsWithOrdinal(ownerName + "-"))
		{
			repoName = repoName.RemovePrefix(ownerName + "-");
		}
		else if (repoName.StartsWithOrdinal(ownerName + "."))
		{
			repoName = repoName.RemovePrefix(ownerName + ".");
		}
		else if (repoName.StartsWithOrdinal(ownerName + "_"))
		{
			repoName = repoName.RemovePrefix(ownerName + "_");
		}

		return repoName;
	}

	private static bool RenderBuildBranchRowColumns(Build build, BranchName branch, List<Run> branchRuns, Run latestRun)
	{
		bool isOngoing = latestRun.IsOngoing;
		// Use branch-specific estimation for more accurate estimates
		TimeSpan estimate = build.CalculateEstimatedDuration(branch);
		TimeSpan duration = isOngoing ? DateTimeOffset.UtcNow - latestRun.Started : latestRun.Duration;
		TimeSpan eta = duration < estimate ? estimate - duration : TimeSpan.Zero;
		double progress = estimate > TimeSpan.Zero ? duration.TotalSeconds / estimate.TotalSeconds : 0;

		bool shouldOpenContextMenu = false;

		ImGui.TableNextRow();
		shouldOpenContextMenu |= RenderStatusColumn(build, latestRun);
		shouldOpenContextMenu |= RenderTextColumn(build.Owner.Name);
		shouldOpenContextMenu |= RenderTextColumn(MakeRepositoryDisplayName(build));
		shouldOpenContextMenu |= RenderTextColumn(MakeBuildDisplayName(build));
		shouldOpenContextMenu |= RenderTextColumn(branch);
		shouldOpenContextMenu |= RenderTextColumn($"{latestRun.Status}");
		shouldOpenContextMenu |= RenderTextColumn(latestRun.Started.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture));
		shouldOpenContextMenu |= RenderDurationColumn(duration);
		shouldOpenContextMenu |= RenderEstimateColumn(estimate);
		shouldOpenContextMenu |= RenderHistoryColumn(branchRuns);
		shouldOpenContextMenu |= RenderProgressColumn(isOngoing, progress);
		shouldOpenContextMenu |= RenderEtaColumn(isOngoing, eta);
		shouldOpenContextMenu |= RenderErrorsColumnIfVisible(latestRun, build, branch);

		return shouldOpenContextMenu;
	}

	private static bool RenderStatusColumn(Build build, Run latestRun)
	{
		bool shouldOpenContextMenu = false;
		if (ImGui.TableNextColumn())
		{
			ImColor statusColor = IsBuildUpdating(build)
				? Color.Palette.Basic.Cyan
				: GetStatusColor(latestRun.Status);
			ImGuiWidgets.ColorIndicator(statusColor, true);
			shouldOpenContextMenu = ImGui.IsItemClicked(ImGuiMouseButton.Right);
		}

		return shouldOpenContextMenu;
	}

	private static bool RenderTextColumn(string text)
	{
		bool shouldOpenContextMenu = false;
		if (ImGui.TableNextColumn())
		{
			ImGui.TextUnformatted(text);
			shouldOpenContextMenu = ImGui.IsItemClicked(ImGuiMouseButton.Right);
		}

		return shouldOpenContextMenu;
	}

	private static bool RenderDurationColumn(TimeSpan duration)
	{
		bool shouldOpenContextMenu = false;
		if (ImGui.TableNextColumn())
		{
			string format = MakeDurationFormat(duration);
			ImGui.TextUnformatted(duration.ToString(format, CultureInfo.InvariantCulture));
			shouldOpenContextMenu = ImGui.IsItemClicked(ImGuiMouseButton.Right);
		}

		return shouldOpenContextMenu;
	}

	private static bool RenderEstimateColumn(TimeSpan estimate)
	{
		bool shouldOpenContextMenu = false;
		if (ImGui.TableNextColumn())
		{
			if (estimate > TimeSpan.Zero)
			{
				string format = MakeDurationFormat(estimate);
				ImGui.TextUnformatted(estimate.ToString(format, CultureInfo.InvariantCulture));
			}
			else
			{
				ImGui.Dummy(new(1, 1));
			}

			shouldOpenContextMenu = ImGui.IsItemClicked(ImGuiMouseButton.Right);
		}

		return shouldOpenContextMenu;
	}

	private static bool RenderHistoryColumn(List<Run> branchRuns)
	{
		bool shouldOpenContextMenu = false;
		if (ImGui.TableNextColumn())
		{
			ShowBranchHistory(branchRuns);
			shouldOpenContextMenu = ImGui.IsItemClicked(ImGuiMouseButton.Right);
		}

		return shouldOpenContextMenu;
	}

	private static bool RenderProgressColumn(bool isOngoing, double progress)
	{
		bool shouldOpenContextMenu = false;
		if (ImGui.TableNextColumn())
		{
			if (isOngoing)
			{
				ImGui.ProgressBar((float)progress, new(-1, ImGui.GetFrameHeight()), $"{progress:P0}");
			}
			else
			{
				ImGui.Dummy(new(1, 1));
			}

			shouldOpenContextMenu = ImGui.IsItemClicked(ImGuiMouseButton.Right);
		}

		return shouldOpenContextMenu;
	}

	private static bool RenderEtaColumn(bool isOngoing, TimeSpan eta)
	{
		bool shouldOpenContextMenu = false;
		if (ImGui.TableNextColumn())
		{
			if (isOngoing)
			{
				string format = MakeDurationFormat(eta);
				ImGui.TextUnformatted(eta > TimeSpan.Zero ? eta.ToString(format, CultureInfo.InvariantCulture) : "???");
			}
			else
			{
				ImGui.Dummy(new(1, 1));
			}

			shouldOpenContextMenu = ImGui.IsItemClicked(ImGuiMouseButton.Right);
		}

		return shouldOpenContextMenu;
	}

	private static bool RenderErrorsColumnIfVisible(Run latestRun, Build build, BranchName branch)
	{
		bool shouldOpenContextMenu = false;
		if (ImGui.TableNextColumn())
		{
			shouldOpenContextMenu = RenderErrorsColumn(latestRun, build, branch);
		}

		return shouldOpenContextMenu;
	}

	private static void RenderBuildBranchContextMenu(Build build, BranchName branch, Run latestRun, bool shouldOpenContextMenu)
	{
		string contextMenuId = $"ContextMenu_{build.Owner.Name}_{build.Repository.Name}_{build.Name}_{branch}";

		if (shouldOpenContextMenu)
		{
			ImGui.OpenPopup(contextMenuId);
		}

		if (ImGui.BeginPopup(contextMenuId))
		{
			RenderRepositoryContextMenuItems(build);
			RenderWorkflowContextMenuItems(build);
			RenderBranchContextMenuItems(build, branch);
			RenderLatestRunContextMenuItems(build, branch, latestRun);
			RenderRefreshContextMenuItem(build);
			ImGui.EndPopup();
		}
	}

	private static void RenderRepositoryContextMenuItems(Build build)
	{
		ImGui.SeparatorText("Repository");
		if (ImGui.MenuItem("Open Repository in Browser"))
		{
			OpenRepositoryInBrowser(build);
		}
		if (ImGui.MenuItem("Copy Repository URL"))
		{
			CopyRepositoryUrl(build);
		}
	}

	private static void RenderWorkflowContextMenuItems(Build build)
	{
		ImGui.Separator();
		ImGui.SeparatorText("Workflow");
		if (ImGui.MenuItem("Open Workflow in Browser"))
		{
			OpenWorkflowInBrowser(build);
		}
		if (ImGui.MenuItem("Copy Workflow URL"))
		{
			CopyWorkflowUrl(build);
		}
	}

	private static void RenderBranchContextMenuItems(Build build, BranchName branch)
	{
		ImGui.Separator();
		ImGui.SeparatorText("Branch");
		if (ImGui.MenuItem("Open Branch in Browser"))
		{
			OpenBranchInBrowser(build, branch);
		}
		if (ImGui.MenuItem("Copy Branch URL"))
		{
			CopyBranchUrl(build, branch);
		}
	}

	private static void RenderLatestRunContextMenuItems(Build build, BranchName branch, Run latestRun)
	{
		ImGui.Separator();
		ImGui.SeparatorText("Latest Run");
		if (ImGui.MenuItem("Open Latest Run in Browser"))
		{
			OpenRunInBrowser(latestRun);
		}
		if (ImGui.MenuItem("Copy Latest Run URL"))
		{
			CopyRunUrl(latestRun);
		}

		if (build.Owner.BuildProvider is GitHub gitHubProvider)
		{
			RenderGitHubApiContextMenuItems(gitHubProvider, build, branch, latestRun);
		}
	}

	private static void RenderGitHubApiContextMenuItems(GitHub gitHubProvider, Build build, BranchName branch, Run latestRun)
	{
		ImGui.Separator();
		if (!latestRun.IsOngoing && ImGui.MenuItem("Re-run Latest Workflow"))
		{
			RerunLatestWorkflow(gitHubProvider, latestRun, build);
		}
		if (latestRun.IsOngoing && ImGui.MenuItem("Cancel Running Workflow"))
		{
			CancelRunningWorkflow(gitHubProvider, latestRun, build);
		}
		if (ImGui.MenuItem("Trigger Workflow on Branch"))
		{
			TriggerWorkflowOnBranch(gitHubProvider, build, branch);
		}
	}

	private static void RenderRefreshContextMenuItem(Build build)
	{
		ImGui.Separator();
		if (ImGui.MenuItem("Refresh Build Data"))
		{
			RefreshBuildData(build);
		}
	}

	private static void OpenRepositoryInBrowser(Build build)
	{
		if (build.Owner.BuildProvider is GitHub)
		{
			OpenUrl(GetRepositoryUrl(build));
		}
	}

	private static void CopyRepositoryUrl(Build build)
	{
		if (build.Owner.BuildProvider is GitHub)
		{
			ImGui.SetClipboardText(GetRepositoryUrl(build));
		}
	}

	private static void OpenWorkflowInBrowser(Build build)
	{
		if (build.Owner.BuildProvider is GitHub)
		{
			OpenUrl(GetWorkflowUrl(build));
		}
	}

	private static void CopyWorkflowUrl(Build build)
	{
		if (build.Owner.BuildProvider is GitHub)
		{
			ImGui.SetClipboardText(GetWorkflowUrl(build));
		}
	}

	private static void OpenBranchInBrowser(Build build, BranchName branch)
	{
		if (build.Owner.BuildProvider is GitHub)
		{
			OpenUrl(GetBranchUrl(build, branch));
		}
	}

	private static void CopyBranchUrl(Build build, BranchName branch)
	{
		if (build.Owner.BuildProvider is GitHub)
		{
			ImGui.SetClipboardText(GetBranchUrl(build, branch));
		}
	}

	private static void OpenRunInBrowser(Run run)
	{
		if (run.Owner.BuildProvider is GitHub)
		{
			OpenUrl(GetRunUrl(run));
		}
	}

	private static void CopyRunUrl(Run run)
	{
		if (run.Owner.BuildProvider is GitHub)
		{
			ImGui.SetClipboardText(GetRunUrl(run));
		}
	}

	private static string GetRepositoryUrl(Build build) => $"https://github.com/{build.Owner.Name}/{build.Repository.Name}";

	private static string GetWorkflowUrl(Build build) => $"https://github.com/{build.Owner.Name}/{build.Repository.Name}/actions/workflows/{build.Id}";

	private static string GetBranchUrl(Build build, BranchName branch) => $"https://github.com/{build.Owner.Name}/{build.Repository.Name}/tree/{branch}";

	private static string GetRunUrl(Run run) => $"https://github.com/{run.Owner.Name}/{run.Repository.Name}/actions/runs/{run.Id}";

	private static void RefreshBuildData(Build build)
	{
		// Queue the build for immediate update
		if (BuildSyncCollection.TryGetValue(build.Id, out BuildSync? buildSync))
		{
			buildSync.ResetTimer();
		}
	}

	private static void RerunLatestWorkflow(GitHub gitHubProvider, Run latestRun, Build build) =>
		ExecuteGitHubApiAction(async () => await gitHubProvider.RerunWorkflowAsync(latestRun).ConfigureAwait(false), build);

	private static void CancelRunningWorkflow(GitHub gitHubProvider, Run latestRun, Build build) =>
		ExecuteGitHubApiAction(async () => await gitHubProvider.CancelWorkflowAsync(latestRun).ConfigureAwait(false), build);

	private static void TriggerWorkflowOnBranch(GitHub gitHubProvider, Build build, BranchName branch) =>
		ExecuteGitHubApiAction(async () => await gitHubProvider.TriggerWorkflowAsync(build, branch).ConfigureAwait(false), build);

	private static void ExecuteGitHubApiAction(Func<Task<bool>> apiAction, Build build)
	{
		_ = Task.Run(async () =>
		{
			try
			{
				bool success = await apiAction().ConfigureAwait(false);
				if (success)
				{
					// Trigger immediate refresh of the build data
					RefreshBuildData(build);
				}
			}
			catch (Octokit.ApiException ex)
			{
				// Log the error but don't crash the application
				Console.WriteLine($"GitHub API action failed: {ex.Message}");
			}
		});
	}

	private static void OpenUrl(string url)
	{
		try
		{
			// Use platform-specific command to open URL
			if (OperatingSystem.IsWindows())
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = url,
					UseShellExecute = true
				});
			}
			else if (OperatingSystem.IsLinux())
			{
				// S4036: xdg-open is the standard way to open URLs on Linux
#pragma warning disable S4036
				Process.Start("xdg-open", url);
#pragma warning restore S4036
			}
			else if (OperatingSystem.IsMacOS())
			{
				// S4036: open is the standard way to open URLs on macOS
#pragma warning disable S4036
				Process.Start("open", url);
#pragma warning restore S4036
			}
		}
#pragma warning disable CA1031 // Do not catch general exception types
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or PlatformNotSupportedException)
#pragma warning restore CA1031 // Do not catch general exception types
		{
			Console.WriteLine($"Failed to open URL '{url}': {ex.Message}");
		}
	}

	private static bool RenderErrorsColumn(Run run, Build build, BranchName branch)
	{
		if (run.Errors.Count == 0)
		{
			ImGui.Dummy(new(1, 1));
			return ImGui.IsItemClicked(ImGuiMouseButton.Right);
		}

		string errorSummary = string.Join("; ", run.Errors);
		float columnWidth = ImGui.GetColumnWidth();

		// Calculate how much text fits in the column
		System.Numerics.Vector2 textSize = ImGui.CalcTextSize(errorSummary);
		string displayText = errorSummary;
		if (textSize.X > columnWidth - 10)
		{
			// Ellipsize the text
			const string ellipsis = "...";
			float ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;
			float availableWidth = columnWidth - ellipsisWidth - 10;

			int charCount = 0;
			float currentWidth = 0;
			foreach (char c in errorSummary)
			{
				float charWidth = ImGui.CalcTextSize(c.ToString()).X;
				if (currentWidth + charWidth > availableWidth)
				{
					break;
				}
				currentWidth += charWidth;
				charCount++;
			}
			displayText = errorSummary[..charCount] + ellipsis;
		}

		bool shouldOpenContextMenu = false;

		// Make the text clickable (use run ID for unique widget ID)
		using (new ScopedTextColor(Color.Palette.Basic.Red))
		{
			if (ImGui.Selectable($"{displayText}##{run.Id}"))
			{
				string fullErrorText = $"Build: {build.Name}\nBranch: {branch}\n\nErrors:\n" + string.Join("\n\n", run.Errors);
				CurrentErrorDetails = fullErrorText;
				ErrorDetailsPopup.Open(
					Strings.ErrorDetails,
					CurrentErrorDetails,
					new Dictionary<string, Action?>
					{
						{ Strings.OK, null }
					},
					ImGuiPopups.PromptTextLayoutType.Wrapped,
					new System.Numerics.Vector2(600, 400));
			}

			shouldOpenContextMenu = ImGui.IsItemClicked(ImGuiMouseButton.Right);
		}

		// Show tooltip on hover
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip(errorSummary);
		}

		return shouldOpenContextMenu;
	}

	private static bool IsBuildUpdating(Build build)
	{
		string prefix = $"{build.Repository.Owner.BuildProvider.Name}/{build.Owner.Name}/{build.Repository.Name}/{build.Name}";
		return ActiveRequests.Keys.Any(k => k.StartsWithOrdinal(prefix));
	}

	private static ImColor GetStatusColor(RunStatus status)
	{
		return status switch
		{
			RunStatus.Pending => Color.Palette.Neutral.Gray,
			RunStatus.Running => Color.Palette.Basic.Yellow,
			RunStatus.Canceled => Color.Palette.Basic.Red,
			RunStatus.Success => Color.Palette.Basic.Green,
			RunStatus.Failure => Color.Palette.Basic.Red,
			_ => Color.Palette.Neutral.Gray,
		};
	}

	private static ImColor GetProviderStatusColor(ProviderStatus status)
	{
		return status switch
		{
			ProviderStatus.OK => Color.Palette.Basic.Green,
			ProviderStatus.RateLimited => Color.Palette.Basic.Yellow,
			ProviderStatus.AuthFailed => Color.Palette.Basic.Red,
			ProviderStatus.Error => Color.Palette.Basic.Magenta,
			_ => Color.Palette.Neutral.Gray,
		};
	}

	private static string GetProviderStatusLabel(ProviderStatus status)
	{
		return status switch
		{
			ProviderStatus.OK => Strings.OK,
			ProviderStatus.RateLimited => Strings.RateLimited,
			ProviderStatus.AuthFailed => Strings.AuthFailed,
			ProviderStatus.Error => Strings.ConnectionError,
			_ => Strings.Status,
		};
	}

	private static void RenderProviderStatusBar()
	{
		foreach ((BuildProviderName _, BuildProvider? provider) in AppData.BuildProviders)
		{
			ImGuiWidgets.ColorIndicator(GetProviderStatusColor(provider.Status), true);
			ImGui.SameLine();

			// Build status text with rate limit info if available
			string statusText = $"{provider.Name}: {GetProviderStatusLabel(provider.Status)}";
			if (provider.RateLimitDisplay != null)
			{
				statusText += $" [{provider.RateLimitDisplay}]";
			}
			ImGui.TextUnformatted(statusText);

			// Show detailed tooltip with status message and rate limit details
			if (ImGui.IsItemHovered())
			{
				string? tooltip = BuildProviderTooltip(provider);
				if (!string.IsNullOrEmpty(tooltip))
				{
					ImGui.SetTooltip(tooltip);
				}
			}

			ImGui.SameLine();
			ImGui.Spacing();
			ImGui.SameLine();
		}
		ImGui.NewLine();
	}

	private static string? BuildProviderTooltip(BuildProvider provider)
	{
		List<string> lines = [];

		if (!string.IsNullOrEmpty(provider.StatusMessage))
		{
			lines.Add(provider.StatusMessage);
		}

		if (provider.RateLimitDetailedStatus != null)
		{
			lines.Add(provider.RateLimitDetailedStatus);
		}

		return lines.Count > 0 ? string.Join("\n", lines) : null;
	}

	private static async Task UpdateAsync()
	{
		if (!ProviderRefreshTimer.IsRunning || ProviderRefreshTimer.Elapsed.TotalSeconds >= ProviderRefreshTimeout)
		{
			// Gather all owners across all providers
			List<(BuildProvider Provider, Owner Owner)> allOwners = [];
			foreach ((BuildProviderName _, BuildProvider? provider) in AppData.BuildProviders)
			{
				foreach ((OwnerName _, Owner? owner) in provider.Owners)
				{
					allOwners.Add((provider, owner));
				}
			}

			// Update repositories concurrently (semaphore limits per-provider concurrency)
			await Task.WhenAll(allOwners.Select(x => x.Provider.UpdateRepositoriesAsync(x.Owner))).ConfigureAwait(false);

			// Gather all repositories across all owners
			List<(BuildProvider Provider, Repository Repository)> allRepositories = [];
			foreach ((BuildProvider provider, Owner owner) in allOwners)
			{
				foreach ((RepositoryId _, Repository? repository) in owner.Repositories)
				{
					allRepositories.Add((provider, repository));
				}
			}

			// Update builds concurrently (semaphore limits per-provider concurrency)
			await Task.WhenAll(allRepositories.Select(async x =>
			{
				await x.Provider.UpdateBuildsAsync(x.Repository).ConfigureAwait(false);

				// Add new builds to sync collection
				foreach ((BuildId _, Build? build) in x.Repository.Builds)
				{
					_ = BuildSyncCollection.TryAdd(build.Id, new()
					{
						Build = build,
					});
				}
			})).ConfigureAwait(false);

			ProviderRefreshTimer.Restart();
		}

		// Update builds and runs concurrently
		await Task.WhenAll(
			UpdateBuildsAsync(),
			UpdateRunsAsync()
		).ConfigureAwait(false);

		PruneOrphanedAndCompletedSyncs();
	}

	private static void PruneOrphanedAndCompletedSyncs()
	{
		// Prune orphaned build syncs (builds that no longer exist in their repository)
		List<KeyValuePair<BuildId, BuildSync>> orphanedBuildSyncs = [.. BuildSyncCollection.Where(b => b.Value.IsOrphaned)];
		foreach ((BuildId? buildId, BuildSync? _) in orphanedBuildSyncs)
		{
			_ = BuildSyncCollection.Remove(buildId, out _);
			Log.Info($"Pruned orphaned build sync: {buildId}");
		}

		// Prune orphaned and completed run syncs
		List<KeyValuePair<RunId, RunSync>> runSyncsToRemove = [.. RunSyncCollection.Where(b => b.Value.IsOrphaned || !b.Value.Run.IsOngoing)];
		foreach ((RunId? runId, RunSync? runSync) in runSyncsToRemove)
		{
			_ = RunSyncCollection.Remove(runId, out _);
			if (runSync.IsOrphaned)
			{
				Log.Info($"Pruned orphaned run sync: {runId}");
			}
		}
	}

	private static async Task UpdateRunsAsync()
	{
		List<KeyValuePair<RunId, RunSync>> runSyncs = [.. RunSyncCollection.Where(b => b.Value.ShouldUpdate)];

		// Update all runs concurrently (semaphore limits per-provider concurrency)
		await Task.WhenAll(runSyncs.Select(kvp => kvp.Value.UpdateAsync())).ConfigureAwait(false);
	}

	private static async Task UpdateBuildsAsync()
	{
		List<KeyValuePair<BuildId, BuildSync>> buildSyncs = [.. BuildSyncCollection.Where(b => b.Value.ShouldUpdate)];

		// Update all builds concurrently (semaphore limits per-provider concurrency)
		await Task.WhenAll(buildSyncs.Select(kvp => kvp.Value.UpdateAsync())).ConfigureAwait(false);
	}

	private static void OnAppMenu()
	{
		if (ImGui.BeginMenu(Strings.File))
		{
			if (ImGui.MenuItem(Strings.ClearData))
			{
				ClearData();
			}

			ImGui.Separator();

			if (ImGui.MenuItem(Strings.Exit))
			{
				ImGuiApp.Stop();
			}

			ImGui.EndMenu();
		}

		if (ImGui.BeginMenu(Strings.Providers))
		{
			lock (SyncLock)
			{
				foreach ((BuildProviderName _, BuildProvider? provider) in AppData.BuildProviders)
				{
					provider.ShowMenu();
				}
			}

			ImGui.EndMenu();
		}
	}

	internal static void QueueSaveAppData() => ShouldSaveAppData = true;

	private static void ClearData()
	{
		BuildSyncCollection.Clear();
		RunSyncCollection.Clear();

		foreach ((BuildProviderName _, BuildProvider? provider) in AppData.BuildProviders)
		{
			provider.ClearData();
		}

		QueueSaveAppData();
	}

	private static void SaveSettingsIfRequired()
	{
		if (ShouldSaveAppData)
		{
			AppData.Save();
			ShouldSaveAppData = false;
		}
	}

	private static void ShowPopupsIfRequired() => _ = ErrorDetailsPopup.ShowIfOpen();

	internal static async Task MakeRequestAsync(string name, Func<Task> action)
	{
		_ = ActiveRequests.TryAdd(name, DateTimeOffset.UtcNow);
		await action.Invoke().ConfigureAwait(false);
		_ = ActiveRequests.TryRemove(name, out _);
	}
}
