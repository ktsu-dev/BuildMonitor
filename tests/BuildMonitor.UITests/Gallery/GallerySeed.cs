// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.UITests.Gallery;

using System.Collections.Concurrent;

using ktsu.Semantics.Strings;

using MonitorAppData = ktsu.BuildMonitor.AppData;

/// <summary>The owners, repositories, builds and runs the gallery photographs.</summary>
/// <remarks>
/// Everything is invented and every time is an offset from <see cref="Now"/>, which the gallery also
/// stops the application's clock at. So a running build is always four minutes in, a countdown
/// always reads the same, and nothing depends on when or where the pictures were taken. Nothing here
/// reaches a provider's network client: the application's polling is switched off before it starts.
/// </remarks>
internal static class GallerySeed
{
	/// <summary>The instant the gallery's clock is stopped at.</summary>
	internal static readonly DateTimeOffset Now = new(2026, 3, 14, 15, 30, 0, TimeSpan.Zero);

	/// <summary>The GitHub owner whose tab the gallery shows on its own.</summary>
	internal const string GitHubOwner = "ktsu-dev";

	/// <summary>The second GitHub owner, so the All tab mixes owners.</summary>
	internal const string SecondGitHubOwner = "contoso";

	/// <summary>The Azure DevOps organization.</summary>
	internal const string AzureDevOpsAccount = "fabrikam";

	/// <summary>The run whose errors the error-details picture opens.</summary>
	internal const string FailedRunId = "14093317";

	/// <summary>Builds the application data the gallery starts from.</summary>
	/// <returns>Application data holding both providers and their seeded builds.</returns>
	internal static MonitorAppData Create()
	{
		GitHub gitHub = new();
		AzureDevOps azureDevOps = new((_, _) => throw new InvalidOperationException("The gallery never connects to Azure DevOps."))
		{
			AccountId = AzureDevOpsAccount.As<BuildProviderAccountId>(),
		};

		MonitorAppData appData = new()
		{
			BuildProviders = new ConcurrentDictionary<BuildProviderName, BuildProvider>(StableNameComparer<BuildProviderName>.Instance),
		};
		_ = appData.BuildProviders.TryAdd(GitHub.BuildProviderName, gitHub);
		_ = appData.BuildProviders.TryAdd(AzureDevOps.BuildProviderName, azureDevOps);

		SeedKtsuDev(AddOwner(gitHub, GitHubOwner));
		SeedContoso(AddOwner(gitHub, SecondGitHubOwner));
		SeedFabrikam(AddOwner(azureDevOps, "Platform"));

		return appData;
	}

	/// <summary>Gives each seeded build a poll timer stopped part way through its interval.</summary>
	/// <param name="appData">The data <see cref="Create"/> returned.</param>
	internal static void FreezePollTimers(MonitorAppData appData)
	{
		int index = 0;
		foreach (Build build in appData.BuildProviders.Values
			.SelectMany(provider => provider.Owners.Values)
			.SelectMany(owner => owner.Repositories.Values)
			.SelectMany(repository => repository.Builds.Values)
			.OrderBy(build => build.Id.ToString(), StringComparer.Ordinal))
		{
			TimeSpan elapsed = TimeSpan.FromSeconds(7 + (index * 11 % 25));
			_ = BuildMonitor.BuildSyncCollection.TryAdd(build.Id, new BuildSync
			{
				Build = build,
				ElapsedOverride = () => elapsed,
			});
			index++;
		}
	}

	private static void SeedKtsuDev(Owner owner)
	{
		// Running on main, with enough successful history for an estimate, a progress ring and an ETA.
		Build monitor = AddBuild(AddRepository(owner, "BuildMonitor"), "dotnet.yml", "1001");
		AddHistory(monitor, "main", 14090000, [8.1, 7.7, 7.9, 8.3, 7.8], firstDaysAgo: 3);
		AddRun(monitor, "14093402", "main", RunStatus.Running, Now.AddMinutes(-4).AddSeconds(-12), TimeSpan.Zero);

		Build imGuiApp = AddBuild(AddRepository(owner, "ImGuiApp"), "dotnet.yml", "1002");
		AddHistory(imGuiApp, "main", 14080000, [12.4, 11.9, 12.8], firstDaysAgo: 2);
		AddRun(imGuiApp, "14093288", "main", RunStatus.Success, Now.AddMinutes(-38), TimeSpan.FromMinutes(12).Add(TimeSpan.FromSeconds(31)));

		// A pull request branch that failed, with the errors the provider read from the job log.
		Build semantics = AddBuild(AddRepository(owner, "Semantics"), "dotnet.yml", "1003");
		AddHistory(semantics, "main", 14070000, [9.6, 9.2, 9.9], firstDaysAgo: 4);
		AddRun(semantics, "14093290", "feature/vector-units", RunStatus.Success, Now.AddHours(-2), TimeSpan.FromMinutes(9).Add(TimeSpan.FromSeconds(48)));
		Run failed = AddRun(semantics, FailedRunId, "feature/vector-units", RunStatus.Failure, Now.AddMinutes(-22), TimeSpan.FromMinutes(6).Add(TimeSpan.FromSeconds(2)));
		failed.Errors =
		[
			"[build] Semantics.Quantities/Generated/Length.g.cs(41,16): error CS0029: Cannot implicitly convert type 'double' to 'Length<double>'",
			"[build] Semantics.Quantities/Generated/Area.g.cs(58,20): error CS0019: Operator '*' cannot be applied to operands of type 'Length<double>' and 'Length<double>'",
			"[build] Process completed with exit code 1.",
		];

		Build keybinding = AddBuild(AddRepository(owner, "Keybinding"), "release.yml", "1004");
		AddHistory(keybinding, "main", 14030000, [3.4, 3.1, 3.6], firstDaysAgo: 9);
		AddRun(keybinding, "14093411", "main", RunStatus.Pending, Now.AddSeconds(-40), TimeSpan.Zero);

		Build themes = AddBuild(AddRepository(owner, "ThemeProvider"), "dotnet.yml", "1005");
		AddHistory(themes, "main", 14060000, [5.2, 5.5, 5.1], firstDaysAgo: 5);
		AddRun(themes, "14093105", "main", RunStatus.Canceled, Now.AddHours(-3).AddMinutes(-10), TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(14)));

		Repository infrastructure = AddRepository(owner, "infrastructure");
		infrastructure.IsPrivate = true;
		Build terraform = AddBuild(infrastructure, "terraform.yml", "1006");
		AddRun(terraform, "14092877", "main", RunStatus.Success, Now.AddHours(-5).AddMinutes(-2), TimeSpan.FromMinutes(3).Add(TimeSpan.FromSeconds(9)));

		Repository fork = AddRepository(owner, "Hexa.NET.ImGui");
		fork.IsFork = true;
		Build forkBuild = AddBuild(fork, "build.yml", "1007");
		AddRun(forkBuild, "14081766", "master", RunStatus.Failure, Now.AddDays(-1).AddHours(-4), TimeSpan.FromMinutes(17).Add(TimeSpan.FromSeconds(40)));

		// A repository with no workflows, which the table still lists.
		_ = AddRepository(owner, "Sandbox");
	}

	private static void SeedContoso(Owner owner)
	{
		Build deploy = AddBuild(AddRepository(owner, "storefront"), "deploy.yml", "2001");
		AddHistory(deploy, "release/2.4", 14050000, [14.8, 15.6, 15.1, 14.6], firstDaysAgo: 6);
		AddRun(deploy, "14093371", "release/2.4", RunStatus.Running, Now.AddMinutes(-11).AddSeconds(-5), TimeSpan.Zero);
	}

	private static void SeedFabrikam(Owner project)
	{
		Repository repository = AddRepository(project, "Platform");
		Build ci = AddBuild(repository, "Platform-CI", "3001");
		AddHistory(ci, "main", 20260300, [21.0, 22.5, 20.4], firstDaysAgo: 3);
		AddRun(ci, "20260391", "main", RunStatus.Success, Now.AddMinutes(-55), TimeSpan.FromMinutes(21).Add(TimeSpan.FromSeconds(12)));

		Build nightly = AddBuild(repository, "Platform-Nightly", "3002");
		AddRun(nightly, "20260377", "main", RunStatus.Failure, Now.AddHours(-9).AddMinutes(-30), TimeSpan.FromMinutes(48).Add(TimeSpan.FromSeconds(3)));
	}

	private static Owner AddOwner(BuildProvider provider, string name)
	{
		Owner owner = provider.CreateOwner(name.As<OwnerName>());
		_ = provider.Owners.TryAdd(owner.Name, owner);
		return owner;
	}

	private static Repository AddRepository(Owner owner, string name)
	{
		Repository repository = owner.CreateRepository(name.As<RepositoryName>());
		_ = owner.Repositories.TryAdd(repository.Id, repository);
		return repository;
	}

	private static Build AddBuild(Repository repository, string name, string id)
	{
		Build build = repository.CreateBuild(name.As<BuildName>(), id.As<BuildId>());
		_ = repository.Builds.TryAdd(build.Id, build);
		return build;
	}

	/// <summary>Adds successful runs a day apart, oldest first, ending the given number of days ago.</summary>
	private static void AddHistory(Build build, string branch, int firstRunId, double[] minutes, int firstDaysAgo)
	{
		for (int i = 0; i < minutes.Length; i++)
		{
			DateTimeOffset started = Now.AddDays(-firstDaysAgo).AddDays(-(minutes.Length - 1 - i) * 0.5);
			AddRun(build, (firstRunId + i).ToString(System.Globalization.CultureInfo.InvariantCulture), branch, RunStatus.Success, started, TimeSpan.FromMinutes(minutes[i]));
		}
	}

	private static Run AddRun(Build build, string id, string branch, RunStatus status, DateTimeOffset started, TimeSpan duration)
	{
		Run run = build.CreateRun($"#{id}".As<RunName>(), id.As<RunId>());
		run.Status = status;
		run.Branch = branch.As<BranchName>();
		run.Started = started;
		run.LastUpdated = status is RunStatus.Pending or RunStatus.Running ? Now : started + duration;
		_ = build.Runs.TryAdd(run.Id, run);
		build.UpdateFromRun(run);
		return run;
	}
}
