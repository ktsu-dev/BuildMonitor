// Ignore Spelling: App

namespace ktsu.io.BuildMonitor;

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

	private static Dictionary<BuildId, BuildSync> BuildSyncCollection { get; } = [];
	internal static Dictionary<RunId, RunSync> RunSyncCollection { get; } = [];

	private static Task SyncTask { get; set; } = new(() => { });

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
		lock (SyncLock)
		{
			needsSave |= AppData.BuildProviders.TryAdd(GitHub.BuildProviderName, new GitHub());
		}
		// add more providers here as needed

		if (needsSave)
		{
			QueueSaveAppData();
		}

		SyncTask = RunSyncQueue();
	}

	private static void Tick(float dt)
	{
		if (SyncTask.IsCompleted)
		{
			if (SyncTask.IsFaulted && SyncTask.Exception is not null)
			{
				throw SyncTask.Exception;
			}

			SyncTask = RunSyncQueue();
		}

		lock (SyncLock)
		{
			var builds = AppData.BuildProviders
				.SelectMany(p => p.Value.Owners)
				.SelectMany(o => o.Value.Repositories)
				.SelectMany(r => r.Value.Builds)
				.OrderByDescending(b => b.Value.LastStarted);

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
					bool isOngoing = build.Runs.Count > 0 && build.IsOngoing;
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

	private static async Task RunSyncQueue()
	{
		if (ProviderRefreshTimer.Elapsed.TotalSeconds >= ProviderRefreshTimeout)
		{
			List<KeyValuePair<BuildProviderName, BuildProvider>> providers;
			lock (SyncLock)
			{
				providers = [.. AppData.BuildProviders];
			}
			foreach (var (_, provider) in providers)
			{
				List<KeyValuePair<OwnerName, Owner>> owners;
				lock (SyncLock)
				{
					owners = [.. provider.Owners];
				}
				foreach (var (_, owner) in owners)
				{
					await provider.SyncRepositoriesAsync(owner);
					List<KeyValuePair<RepositoryId, Repository>> repositories;
					lock (SyncLock)
					{
						repositories = [.. owner.Repositories];
					}
					foreach (var (_, repository) in repositories)
					{
						await provider.SyncBuildsAsync(repository);
						List<KeyValuePair<BuildId, Build>> builds;
						lock (SyncLock)
						{
							builds = [.. repository.Builds];
						}
						foreach (var (_, build) in builds)
						{
							lock (SyncLock)
							{
								_ = BuildSyncCollection.TryAdd(build.Id, new()
								{
									Build = build,
								});
							}
						}
					}
				}
			}
		}

		List<KeyValuePair<BuildId, BuildSync>> buildSyncs;
		lock (SyncLock)
		{
			buildSyncs = BuildSyncCollection.Where(b => b.Value.ShouldSync).ToList();
		}

		foreach (var (buildId, buildSync) in buildSyncs)
		{
			await buildSync.Sync();
		}

		List<KeyValuePair<RunId, RunSync>> runSyncs;
		lock (SyncLock)
		{
			runSyncs = RunSyncCollection.Where(b => b.Value.ShouldSync).ToList();
		}

		foreach (var (runId, runSync) in runSyncs)
		{
			await runSync.Sync();
		}

		List<KeyValuePair<RunId, RunSync>> completedRunSyncs;
		lock (SyncLock)
		{
			completedRunSyncs = RunSyncCollection.Where(b => !b.Value.Run.IsOngoing).ToList();
			foreach (var (runId, runSync) in completedRunSyncs)
			{
				_ = RunSyncCollection.Remove(runId);
			}
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
			foreach (var (_, provider) in AppData.BuildProviders)
			{
				provider.ShowMenu();
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
