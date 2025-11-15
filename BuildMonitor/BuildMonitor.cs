// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Hexa.NET.ImGui;
using ktsu.Extensions;
using ktsu.ImGuiApp;
using ktsu.ImGuiStyler;
using ktsu.ImGuiWidgets;
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
			OnStart = Start,
			OnUpdate = Tick,
			OnAppMenu = ShowMenu,
			OnMoveOrResize = WindowResized
		});
	}

	private static void WindowResized()
	{
		AppData.WindowState = ImGuiApp.WindowState;
		QueueSaveAppData();
	}

	private static void Start()
	{
		var needsSave = false;

		needsSave |= AppData.BuildProviders.TryAdd(GitHub.BuildProviderName, new GitHub());

		// add more providers here as needed

		if (needsSave)
		{
			QueueSaveAppData();
		}

		UpdateTask = UpdateAsync();
	}

	private static string FilterRepository { get; set; } = string.Empty;
	private static string FilterBuildName { get; set; } = string.Empty;
	private static string FilterStatus { get; set; } = string.Empty;

	private static void Tick(float dt)
	{
		if (UpdateTask.IsCompleted)
		{
			if (UpdateTask.IsFaulted && UpdateTask.Exception is not null)
			{
				throw UpdateTask.Exception;
			}

			UpdateTask = UpdateAsync();
		}

		foreach (var (name, buildProvider) in AppData.BuildProviders)
		{
			buildProvider.Tick();
		}

		var builds = AppData.BuildProviders
			.SelectMany(p => p.Value.Owners)
			.SelectMany(o => o.Value.Repositories)
			.SelectMany(r => r.Value.Builds)
			.OrderByDescending(b => b.Value.LastStarted);

		//foreach (var (name, startTime) in ActiveRequests)
		//{
		//	string format = @"hh\:mm\:ss";
		//	var duration = DateTimeOffset.UtcNow - startTime;
		//	ImGui.TextUnformatted($"{name} {duration.ToString(format, CultureInfo.InvariantCulture)}");
		//}

		if (ImGui.BeginTable(Strings.Builds, 8, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
		{
			ImGui.TableSetupColumn("##buildStatus", ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.Repository, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.BuildName, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.Status, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.Duration, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.History, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.Progress, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.ETA, ImGuiTableColumnFlags.WidthStretch);

			ImGui.TableHeadersRow();

			ImGui.TableNextColumn();

			if (ImGui.TableNextColumn())
			{
				var input = FilterRepository;
				ImGui.InputText("##FilterRepository", ref input, 256);
				FilterRepository = input;
			}

			if (ImGui.TableNextColumn())
			{
				var input = FilterBuildName;
				ImGui.InputText("##FilterBuildName", ref input, 256);
				FilterBuildName = input;
			}

			if (ImGui.TableNextColumn())
			{
				var input = FilterStatus;
				ImGui.InputText("##FilterStatus", ref input, 256);
				FilterStatus = input;
			}

			ImGui.TableHeadersRow();

			foreach (var (_, build) in builds)
			{
				var shouldShow = ShouldShowBuild(build);

				if (!shouldShow)
				{
					continue;
				}

				var isOngoing = !build.Runs.IsEmpty && build.IsOngoing;
				var estimate = build.CalculateEstimatedDuration();
				var duration = isOngoing ? DateTimeOffset.UtcNow - build.LastStarted : build.LastDuration;
				var eta = duration < estimate ? estimate - duration : TimeSpan.Zero;
				var progress = duration.TotalSeconds / estimate.TotalSeconds;

				ImGui.TableNextRow();
				if (ImGui.TableNextColumn())
				{
					ImGuiWidgets.ColorIndicator(GetStatusColor(build.LastStatus), true);
				}

				if (ImGui.TableNextColumn())
				{
					ImGui.TextUnformatted($"{build.Owner.Name}/{build.Repository.Name}");
				}

				if (ImGui.TableNextColumn())
				{
					var displayName = MakeBuildDisplayName(build);

					ImGui.TextUnformatted(displayName);
				}

				if (ImGui.TableNextColumn())
				{
					ImGui.TextUnformatted($"{build.LastStatus}");
				}

				if (ImGui.TableNextColumn())
				{
					var format = MakeDurationFormat(duration);

					ImGui.TextUnformatted(duration.ToString(format, CultureInfo.InvariantCulture));
				}

				if (ImGui.TableNextColumn())
				{
					ShowBuildHistory(build);
				}

				if (ImGui.TableNextColumn() && isOngoing)
				{
					ImGui.ProgressBar((float)progress, new(0, ImGui.GetFrameHeight()), $"{progress:P0}");
				}

				if (ImGui.TableNextColumn() && isOngoing)
				{
					var format = MakeDurationFormat(eta);

					ImGui.TextUnformatted(eta > TimeSpan.Zero ? eta.ToString(format, CultureInfo.InvariantCulture) : "???");
				}
			}

			ImGui.EndTable();

			ShowPopupsIfRequired();
			SaveSettingsIfRequired();
		}
	}

	private static string MakeDurationFormat(TimeSpan duration)
	{
		var format = @"hh\:mm\:ss";
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

	private static bool ShouldShowBuild(Build build)
	{
		var shouldShow = true;
		if (!string.IsNullOrEmpty(FilterRepository))
		{
			shouldShow &= TextFilter.IsMatch(build.Repository.Name, FilterRepository);
		}

		if (!string.IsNullOrEmpty(FilterBuildName))
		{
			shouldShow &= TextFilter.IsMatch(build.Name, FilterBuildName);
		}

		if (!string.IsNullOrEmpty(FilterStatus))
		{
			shouldShow &= TextFilter.IsMatch(build.LastStatus.ToString(), FilterStatus);
		}

		return shouldShow;
	}

	private static void ShowBuildHistory(Build build)
	{
		const int recentRuns = 5;

		var runs = build.Runs.Values
			.Where(r => r.Status != RunStatus.Canceled)
			.OrderByDescending(r => r.LastUpdated)
			.Take(recentRuns)
			.ToList();

		runs.Reverse();

		foreach (var run in runs)
		{
			ImGui.SameLine();
			ImGuiWidgets.ColorIndicator(GetStatusColor(run.Status), true);
		}
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
			foreach (var (_, provider) in AppData.BuildProviders)
			{
				foreach (var (_, owner) in provider.Owners)
				{
					await provider.UpdateRepositoriesAsync(owner).ConfigureAwait(false);
					foreach (var (_, repository) in owner.Repositories)
					{
						await provider.UpdateBuildsAsync(repository).ConfigureAwait(false);
						foreach (var (_, build) in repository.Builds)
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
		var completedRunSyncs = RunSyncCollection.Where(b => !b.Value.Run.IsOngoing).ToList();
		foreach (var (runId, runSync) in completedRunSyncs)
		{
			_ = RunSyncCollection.Remove(runId, out _);
		}
	}

	private static async Task UpdateRunsAsync()
	{
		var runSyncs = RunSyncCollection.Where(b => b.Value.ShouldUpdate).ToList();
		foreach (var (runId, runSync) in runSyncs)
		{
			await runSync.UpdateAsync().ConfigureAwait(false);
		}
	}

	private static async Task UpdateBuildsAsync()
	{
		var buildSyncs = BuildSyncCollection.Where(b => b.Value.ShouldUpdate).ToList();
		foreach (var (buildId, buildSync) in buildSyncs)
		{
			await buildSync.UpdateAsync().ConfigureAwait(false);
		}
	}

	private static void ShowMenu()
	{
		if (ImGui.BeginMenu(Strings.File))
		{
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
				foreach (var (_, provider) in AppData.BuildProviders)
				{
					provider.ShowMenu();
				}
			}

			ImGui.EndMenu();
		}
	}

	internal static void QueueSaveAppData() => ShouldSaveAppData = true;

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
