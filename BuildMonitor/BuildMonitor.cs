// Ignore Spelling: App

namespace ktsu.io.BuildMonitor;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using ImGuiNET;
using ktsu.io.ImGuiApp;
using ktsu.io.ImGuiWidgets;

internal static class BuildMonitor
{
	internal static AppData AppData { get; set; } = new();
	private static bool ShouldSaveAppData { get; set; }

	private static Stopwatch ProviderRefreshTimer { get; } = new Stopwatch();

	private const int ProviderRefreshTimeout = 300;

	internal static object SyncLock { get; } = new();

	private static ConcurrentDictionary<BuildId, BuildSync> BuildSyncCollection { get; } = [];
	internal static ConcurrentDictionary<RunId, RunSync> RunSyncCollection { get; } = [];

	private static Task UpdateTask { get; set; } = new(() => { });

	internal static string LastRequest { get; set; } = string.Empty;

	private static void Main()
	{
		AppData = AppData.LoadOrCreate();
		ImGuiApp.Start(Strings.ApplicationName, AppData.WindowState, Start, Tick, ShowMenu, WindowResized);
	}

	private static void WindowResized()
	{
		AppData.WindowState = ImGuiApp.WindowState;
		QueueSaveAppData();
	}

	private static void Start()
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


		var builds = AppData.BuildProviders
			.SelectMany(p => p.Value.Owners)
			.SelectMany(o => o.Value.Repositories)
			.SelectMany(r => r.Value.Builds)
			.OrderByDescending(b => b.Value.LastStarted);

		ImGui.TextUnformatted(LastRequest);

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

			foreach (var (_, build) in builds)
			{
				bool isOngoing = !build.Runs.IsEmpty && build.IsOngoing;
				var estimate = build.CalculateEstimatedDuration();
				var duration = isOngoing ? DateTimeOffset.UtcNow - build.LastStarted : build.LastDuration;
				var eta = duration < estimate ? estimate - duration : TimeSpan.Zero;
				double progress = duration.TotalSeconds / estimate.TotalSeconds;

				ImGui.TableNextRow();
				if (ImGui.TableNextColumn())
				{
					ColorIndicator.Show(GetStatusColor(build.LastStatus), true);
				}

				if (ImGui.TableNextColumn())
				{
					ImGui.TextUnformatted($"{build.Owner.Name}/{build.Repository.Name}");
				}

				if (ImGui.TableNextColumn())
				{
					ImGui.TextUnformatted(build.Name);
				}

				if (ImGui.TableNextColumn())
				{
					ImGui.TextUnformatted($"{build.LastStatus}");
				}

				if (ImGui.TableNextColumn())
				{
					string format = @"hh\:mm\:ss";
					if (duration.Days > 0)
					{
						format = @"d\.hh\:mm\:ss";
					}
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
					string format = @"hh\:mm\:ss";
					if (eta.Days > 0)
					{
						format = @"d\.hh\:mm\:ss";
					}
					ImGui.TextUnformatted(eta > TimeSpan.Zero ? eta.ToString(format, CultureInfo.InvariantCulture) : "???");
				}
			}

			ImGui.EndTable();

			ShowPopupsIfRequired();
			SaveSettingsIfRequired();
		}
	}

	private static void ShowBuildHistory(Build build)
	{
		const int recentRuns = 5;

		var runs = build.Runs.Values
		.OrderByDescending(r => r.LastUpdated)
		.Take(recentRuns)
		.ToList();

		runs.Reverse();

		foreach (var run in runs)
		{
			ImGui.SameLine();
			ColorIndicator.Show(GetStatusColor(run.Status), true);
		}
	}

	private static ImColor GetStatusColor(RunStatus status)
	{
		return status switch
		{
			RunStatus.Pending => Color.Gray,
			RunStatus.Running => Color.Yellow,
			RunStatus.Canceled => Color.Red,
			RunStatus.Success => Color.Green,
			RunStatus.Failure => Color.Red,
			_ => Color.Gray,
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
					await provider.UpdateRepositoriesAsync(owner);
					foreach (var (_, repository) in owner.Repositories)
					{
						await provider.UpdateBuildsAsync(repository);
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

		await UpdateBuildsAsync();

		await UpdateRunsAsync();

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
			await runSync.UpdateAsync();
		}
	}

	private static async Task UpdateBuildsAsync()
	{
		var buildSyncs = BuildSyncCollection.Where(b => b.Value.ShouldUpdate).ToList();
		foreach (var (buildId, buildSync) in buildSyncs)
		{
			await buildSync.UpdateAsync();
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
}
