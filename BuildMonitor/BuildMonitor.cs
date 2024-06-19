// Ignore Spelling: App

namespace ktsu.io.BuildMonitor;

using System.Diagnostics;
using Humanizer;
using ImGuiNET;
using ktsu.io.ImGuiApp;
using ktsu.io.ImGuiWidgets;

internal enum SyncStage
{
	NotStarted,
	Repositories,
	Builds,
	Runs,
	Finished,
}

internal static class BuildMonitor
{
	internal static AppData AppData { get; set; } = new();
	private static bool ShouldSaveAppData { get; set; }

	private static Stopwatch RefreshTimer { get; } = new Stopwatch();

	private const int RefreshTimeout = 30;

	private static Queue<Task> SyncQueue { get; } = [];
	private static SyncStage SyncStage { get; set; }
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
	}

	private static void Tick(float dt)
	{
		foreach (var (_, provider) in AppData.BuildProviders)
		{
			provider.Tick();
		}

		if (!RefreshTimer.IsRunning || RefreshTimer.Elapsed.TotalSeconds >= RefreshTimeout)
		{
			SyncStage = SyncStage.NotStarted;
			RefreshTimer.Restart();
		}

		RunSyncQueue();
		PruneSyncQueue();



		var builds = AppData.BuildProviders
			.SelectMany(p => p.Value.Owners)
			.SelectMany(o => o.Value.Repositories)
			.SelectMany(r => r.Value.Builds)
			.OrderByDescending(b => b.Value.LastStarted)
			.ToList();

		if (ImGui.BeginTable(Strings.Builds, 7, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
		{
			ImGui.TableSetupColumn("##buildStatus", ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.Repository, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.BuildName, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.Status, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.History, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.Progress, ImGuiTableColumnFlags.WidthStretch);
			ImGui.TableSetupColumn(Strings.ETA, ImGuiTableColumnFlags.WidthStretch);

			ImGui.TableHeadersRow();

			foreach (var (_, build) in builds)
			{
				bool isRunning = build.Runs.Count > 0 && build.LastStatus is RunStatus.Pending or RunStatus.Running;
				var estimate = build.CalculateEstimatedDuration();
				var duration = isRunning ? DateTimeOffset.UtcNow - build.LastStarted : build.LastDuration;
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
					//ImGui.TextUnformatted($"{build.Runs.Count} runs");
					ShowBuildHistory(build);
				}
				if (ImGui.TableNextColumn())
				{
					if (isRunning)
					{
						//ImGui.TextUnformatted(build.LastDuration.Humanize());
						ImGui.ProgressBar((float)progress, new(0, ImGui.GetFrameHeight()), $"{progress:P0}");
					}
					else
					{
						ImGui.TextUnformatted(build.LastDuration.Humanize());
					}
				}
				if (ImGui.TableNextColumn() && isRunning)
				{
					ImGui.TextUnformatted(eta > TimeSpan.Zero ? eta.Humanize() : "???");
				}
			}
		}
		ImGui.EndTable();

		ShowPopupsIfRequired();
		SaveSettingsIfRequired();
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
			RunStatus.Running => Color.Blue,
			RunStatus.Canceled => Color.Red,
			RunStatus.Success => Color.Green,
			RunStatus.Failure => Color.Red,
			_ => Color.Gray,
		};
	}

	private static void RunSyncQueue()
	{
		switch (SyncStage)
		{
			case SyncStage.NotStarted:
				if (SyncQueue.Count == 0)
				{
					SyncStage = SyncStage.Repositories;
					foreach (var (_, provider) in AppData.BuildProviders)
					{
						foreach (var (_, owner) in provider.Owners)
						{
							SyncQueue.Enqueue(provider.SyncRepositoriesAsync(owner));
						}
					}
				}
				break;
			case SyncStage.Repositories:
				if (SyncQueue.Count == 0)
				{
					SyncStage = SyncStage.Builds;
					foreach (var (_, provider) in AppData.BuildProviders)
					{
						foreach (var (_, owner) in provider.Owners)
						{
							foreach (var (_, repository) in owner.Repositories)
							{
								SyncQueue.Enqueue(provider.SyncBuildsAsync(repository));
							}
						}
					}
				}
				break;
			case SyncStage.Builds:
				if (SyncQueue.Count == 0)
				{
					SyncStage = SyncStage.Runs;
					foreach (var (_, provider) in AppData.BuildProviders)
					{
						foreach (var (_, owner) in provider.Owners)
						{
							foreach (var (_, repository) in owner.Repositories)
							{
								SyncQueue.Enqueue(provider.SyncRunsAsync(repository));
								foreach (var (_, build) in repository.Builds)
								{
									SyncQueue.Enqueue(provider.SyncRunsAsync(build));
								}
							}
						}
					}
				}
				break;
			case SyncStage.Runs:
				if (SyncQueue.Count == 0)
				{
					SyncStage = SyncStage.Finished;
				}
				break;
			case SyncStage.Finished:
				QueueSaveAppData();
				break;
			default:
				break;
		}
	}

	private static void PruneSyncQueue()
	{
		while (SyncQueue.Count > 0)
		{
			if (SyncQueue.Peek().IsCompleted)
			{
				SyncQueue.Dequeue();
			}
			else
			{
				break;
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
