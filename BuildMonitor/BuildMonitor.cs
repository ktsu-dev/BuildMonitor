// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Hexa.NET.ImGui;
using ktsu.Extensions;
using ktsu.ImGui.App;
using ktsu.ImGui.Styler;
using ktsu.ImGui.Widgets;
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

	private static string FilterRepository = string.Empty;
	private static TextFilterType FilterRepositoryType = TextFilterType.Glob;
	private static TextFilterMatchOptions FilterRepositoryMatchOptions = TextFilterMatchOptions.ByWordAny;

	private static string FilterBuildName = string.Empty;
	private static TextFilterType FilterBuildNameType = TextFilterType.Glob;
	private static TextFilterMatchOptions FilterBuildNameMatchOptions = TextFilterMatchOptions.ByWordAny;

	private static string FilterStatus = string.Empty;
	private static TextFilterType FilterStatusType = TextFilterType.Glob;
	private static TextFilterMatchOptions FilterStatusMatchOptions = TextFilterMatchOptions.ByWordAny;

	private static string FilterBranch = string.Empty;
	private static TextFilterType FilterBranchType = TextFilterType.Glob;
	private static TextFilterMatchOptions FilterBranchMatchOptions = TextFilterMatchOptions.ByWordAny;

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

		//foreach (var (name, startTime) in ActiveRequests)
		//{
		//	string format = @"hh\:mm\:ss";
		//	var duration = DateTimeOffset.UtcNow - startTime;
		//	ImGui.TextUnformatted($"{name} {duration.ToString(format, CultureInfo.InvariantCulture)}");
		//}

		if (ImGui.BeginTable(Strings.Builds, 9, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
		{
			ImGui.TableSetupColumn("##buildStatus", ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.Repository, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.BuildName, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.Branch, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.Status, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.Duration, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.History, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.Progress, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.ETA, ImGuiTableColumnFlags.WidthStretch);

			ImGui.TableHeadersRow();

			// Filter row
			ImGui.TableNextRow();
			ImGui.TableNextColumn(); // Skip status column

			if (ImGui.TableNextColumn())
			{
				ImGuiWidgets.SearchBox("##FilterRepository", ref FilterRepository, ref FilterRepositoryType, ref FilterRepositoryMatchOptions);
			}

			if (ImGui.TableNextColumn())
			{
				ImGuiWidgets.SearchBox("##FilterBuildName", ref FilterBuildName, ref FilterBuildNameType, ref FilterBuildNameMatchOptions);
			}

			if (ImGui.TableNextColumn())
			{
				ImGuiWidgets.SearchBox("##FilterBranch", ref FilterBranch, ref FilterBranchType, ref FilterBranchMatchOptions);
			}

			if (ImGui.TableNextColumn())
			{
				ImGuiWidgets.SearchBox("##FilterStatus", ref FilterStatus, ref FilterStatusType, ref FilterStatusMatchOptions);
			}

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
		if (!string.IsNullOrEmpty(FilterRepository))
		{
			string displayRepository = $"{build.Owner.Name}/{build.Repository.Name}";
			shouldShow &= TextFilter.IsMatch(displayRepository.ToUpperInvariant(), "*" + FilterRepository.ToUpperInvariant() + "*", FilterRepositoryType, FilterRepositoryMatchOptions);
		}

		if (!string.IsNullOrEmpty(FilterBuildName))
		{
			string displayName = MakeBuildDisplayName(build);
			shouldShow &= TextFilter.IsMatch(displayName.ToUpperInvariant(), "*" + FilterBuildName.ToUpperInvariant() + "*", FilterBuildNameType, FilterBuildNameMatchOptions);
		}

		if (!string.IsNullOrEmpty(FilterBranch))
		{
			shouldShow &= TextFilter.IsMatch(branch.ToString().ToUpperInvariant(), "*" + FilterBranch.ToUpperInvariant() + "*", FilterBranchType, FilterBranchMatchOptions);
		}

		if (!string.IsNullOrEmpty(FilterStatus))
		{
			Run? latestRun = build.Runs.Values
				.Where(r => r.Branch == branch)
				.OrderByDescending(r => r.Started)
				.FirstOrDefault();
			if (latestRun is not null)
			{
				shouldShow &= TextFilter.IsMatch(latestRun.Status.ToString().ToUpperInvariant(), "*" + FilterStatus.ToUpperInvariant() + "*", FilterStatusType, FilterStatusMatchOptions);
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
			ImGuiWidgets.ColorIndicator(GetStatusColor(latestRun.Status), true);
			if (IsBuildUpdating(build))
			{
				ImGui.SameLine();
				ImGuiWidgets.ColorIndicator(Color.Palette.Basic.Cyan, true);
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
			string format = MakeDurationFormat(duration);
			ImGui.TextUnformatted(duration.ToString(format, CultureInfo.InvariantCulture));
		}

		if (ImGui.TableNextColumn())
		{
			ShowBranchHistory(branchRuns);
		}

		if (ImGui.TableNextColumn() && isOngoing)
		{
			ImGui.ProgressBar((float)progress, new(0, ImGui.GetFrameHeight()), $"{progress:P0}");
		}

		if (ImGui.TableNextColumn() && isOngoing)
		{
			string format = MakeDurationFormat(eta);
			ImGui.TextUnformatted(eta > TimeSpan.Zero ? eta.ToString(format, CultureInfo.InvariantCulture) : "???");
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
			foreach ((BuildProviderName _, BuildProvider? provider) in AppData.BuildProviders)
			{
				foreach ((OwnerName _, Owner? owner) in provider.Owners)
				{
					await provider.UpdateRepositoriesAsync(owner).ConfigureAwait(false);
					foreach ((RepositoryId _, Repository? repository) in owner.Repositories)
					{
						await provider.UpdateBuildsAsync(repository).ConfigureAwait(false);
						foreach ((BuildId _, Build? build) in repository.Builds)
						{
							_ = BuildSyncCollection.TryAdd(build.Id, new()
							{
								Build = build,
							});
						}
					}
				}
			}

			ProviderRefreshTimer.Restart();
		}

		await UpdateBuildsAsync().ConfigureAwait(false);

		await UpdateRunsAsync().ConfigureAwait(false);

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
		foreach ((RunId? runId, RunSync? runSync) in runSyncs)
		{
			await runSync.UpdateAsync().ConfigureAwait(false);
		}
	}

	private static async Task UpdateBuildsAsync()
	{
		List<KeyValuePair<BuildId, BuildSync>> buildSyncs = [.. BuildSyncCollection.Where(b => b.Value.ShouldUpdate)];
		foreach ((BuildId? buildId, BuildSync? buildSync) in buildSyncs)
		{
			await buildSync.UpdateAsync().ConfigureAwait(false);
		}
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

	private static void ShowPopupsIfRequired()
	{
		// none yet
	}

	internal static async Task MakeRequestAsync(string name, Func<Task> action)
	{
		_ = ActiveRequests.TryAdd(name, DateTimeOffset.UtcNow);
		await action.Invoke().ConfigureAwait(false);
		_ = ActiveRequests.TryRemove(name, out _);
	}
}
