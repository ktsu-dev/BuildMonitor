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
		bool needsSave = false;

		needsSave |= AppData.BuildProviders.TryAdd(GitHub.BuildProviderName, new GitHub());

		// add more providers here as needed

		if (needsSave)
		{
			QueueSaveAppData();
		}

		UpdateTask = UpdateAsync();
	}

	private static readonly Dictionary<string, float> DefaultColumnWidths = new()
	{
		["##buildStatus"] = 30f,
		[Strings.Repository] = 200f,
		[Strings.BuildName] = 200f,
		[Strings.Branch] = 150f,
		[Strings.Status] = 80f,
		[Strings.LastRun] = 180f,
		[Strings.Duration] = 80f,
		[Strings.History] = 80f,
		[Strings.Progress] = 100f,
		[Strings.ETA] = 80f,
		[Strings.Errors] = 200f,
	};

	private static ImGuiPopups.Prompt ErrorDetailsPopup { get; } = new();
	private static string CurrentErrorDetails { get; set; } = string.Empty;

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

		foreach ((BuildProviderName? name, BuildProvider? buildProvider) in AppData.BuildProviders)
		{
			buildProvider.Tick();
		}

		if (ImGui.BeginTable(Strings.Builds, 11, ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg))
		{
			foreach (string columnName in DefaultColumnWidths.Keys)
			{
				ImGui.TableSetupColumn(columnName, ImGuiTableColumnFlags.WidthFixed, GetColumnWidth(columnName));
			}

			ImGui.TableHeadersRow();

			RenderFilterRow();

			// Group runs by (Build, Branch) and iterate
			IOrderedEnumerable<(Build Build, BranchName Branch, List<Run> Runs)> buildBranches = AppData.BuildProviders
				.SelectMany(p => p.Value.Owners)
				.SelectMany(o => o.Value.Repositories)
				.SelectMany(r => r.Value.Builds)
				.SelectMany(b => b.Value.Runs.Values
					.GroupBy(r => r.Branch)
					.Select(g => (Build: b.Value, Branch: g.Key, Runs: g.OrderByDescending(r => r.Started).ToList())))
				.OrderByDescending(x => x.Runs.FirstOrDefault()?.Started ?? DateTimeOffset.MinValue);

			foreach ((Build build, BranchName branch, List<Run> branchRuns) in buildBranches)
			{
				RenderBuildBranchRow(build, branch, branchRuns);
			}

			int saveColIndex = 0;
			foreach (string columnName in DefaultColumnWidths.Keys)
			{
				SaveColumnWidth(columnName, saveColIndex);
				saveColIndex++;
			}

			ImGui.EndTable();

			ShowPopupsIfRequired();
			SaveSettingsIfRequired();
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

	private static bool ShouldShowBuildBranch(Build build, BranchName branch)
	{
		bool shouldShow = true;
		if (!string.IsNullOrEmpty(AppData.FilterRepository))
		{
			string displayRepository = $"{build.Owner.Name}/{build.Repository.Name}";
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

		bool isOngoing = latestRun.IsOngoing;
		TimeSpan estimate = build.CalculateEstimatedDuration();
		TimeSpan duration = isOngoing ? DateTimeOffset.UtcNow - latestRun.Started : latestRun.Duration;
		TimeSpan eta = duration < estimate ? estimate - duration : TimeSpan.Zero;
		double progress = duration.TotalSeconds / estimate.TotalSeconds;

		ImGui.TableNextRow();
		if (ImGui.TableNextColumn())
		{
			if (IsBuildUpdating(build))
			{
				ImGuiWidgets.ColorIndicator(Color.Palette.Basic.Cyan, true);
			}
			else
			{
				ImGuiWidgets.ColorIndicator(GetStatusColor(latestRun.Status), true);
			}
		}

		if (ImGui.TableNextColumn())
		{
			ImGui.TextUnformatted($"{build.Owner.Name}/{build.Repository.Name}");
		}

		if (ImGui.TableNextColumn())
		{
			string displayName = MakeBuildDisplayName(build);
			ImGui.TextUnformatted(displayName);
		}

		if (ImGui.TableNextColumn())
		{
			ImGui.TextUnformatted(branch);
		}

		if (ImGui.TableNextColumn())
		{
			ImGui.TextUnformatted($"{latestRun.Status}");
		}

		if (ImGui.TableNextColumn())
		{
			ImGui.TextUnformatted(latestRun.Started.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture));
		}

		if (ImGui.TableNextColumn())
		{
			string format = MakeDurationFormat(duration);
			ImGui.TextUnformatted(duration.ToString(format, CultureInfo.InvariantCulture));
		}

		if (ImGui.TableNextColumn())
		{
			ShowBranchHistory(branchRuns);
		}

		if (ImGui.TableNextColumn() && isOngoing)
		{
			ImGui.ProgressBar((float)progress, new(-1, ImGui.GetFrameHeight()), $"{progress:P0}");
		}

		if (ImGui.TableNextColumn() && isOngoing)
		{
			string format = MakeDurationFormat(eta);
			ImGui.TextUnformatted(eta > TimeSpan.Zero ? eta.ToString(format, CultureInfo.InvariantCulture) : "???");
		}

		if (ImGui.TableNextColumn())
		{
			RenderErrorsColumn(latestRun, build, branch);
		}
	}

	private static void RenderErrorsColumn(Run run, Build build, BranchName branch)
	{
		if (run.Errors.Count == 0)
		{
			return;
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
		}

		// Show tooltip on hover
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip(errorSummary);
		}
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

		PruneCompletedRuns();
	}

	private static void PruneCompletedRuns()
	{
		List<KeyValuePair<RunId, RunSync>> completedRunSyncs = [.. RunSyncCollection.Where(b => !b.Value.Run.IsOngoing)];
		foreach ((RunId? runId, RunSync? runSync) in completedRunSyncs)
		{
			_ = RunSyncCollection.Remove(runId, out _);
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
