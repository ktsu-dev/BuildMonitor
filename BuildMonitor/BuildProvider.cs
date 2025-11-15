// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;
using System.Text.Json.Serialization;

using Hexa.NET.ImGui;

using ktsu.ImGuiPopups;
using ktsu.StrongStrings;

/// <summary>
/// Represents the name of a build provider.
/// </summary>
public sealed record class BuildProviderName : StrongStringAbstract<BuildProviderName> { }
/// <summary>
/// Represents the account ID of a build provider.
/// </summary>
public sealed record class BuildProviderAccountId : StrongStringAbstract<BuildProviderAccountId> { }
/// <summary>
/// Represents the token of a build provider.
/// </summary>
public sealed record class BuildProviderToken : StrongStringAbstract<BuildProviderToken> { }

[JsonDerivedType(typeof(GitHub), nameof(GitHub))]
[JsonPolymorphic]
internal abstract class BuildProvider
{
	internal abstract BuildProviderName Name { get; }
	[JsonInclude]
	protected BuildProviderAccountId AccountId { get; private set; } = new();
	[JsonInclude]
	protected BuildProviderToken Token { get; private set; } = new();
	[JsonInclude]
	internal ConcurrentDictionary<OwnerName, Owner> Owners { get; init; } = [];
	private bool ShouldShowAccountIdPopup { get; set; }
	private bool ShouldShowTokenPopup { get; set; }
	private bool ShouldShowAddOwnerPopup { get; set; }
	private ImGuiPopups.InputString PopupInputString { get; } = new();
	protected TimeSpan RateLimitSleep { get; set; } = TimeSpan.FromMilliseconds(500);

	internal Owner CreateOwner(OwnerName name)
	{
		return new()
		{
			Name = name,
			BuildProvider = this,
			Enabled = true
		};
	}

	internal void ShowMenu()
	{
		if (ImGui.BeginMenu(Name))
		{
			if (ImGui.MenuItem(Strings.SetCredentials))
			{
				ShouldShowAccountIdPopup = true;
			}

			if (ImGui.MenuItem(Strings.AddOwner))
			{
				ShouldShowAddOwnerPopup = true;
			}

			ImGui.EndMenu();
		}
	}

	internal void Tick()
	{
		if (ShouldShowAccountIdPopup)
		{
			PopupInputString.Open(Strings.SetAccountId, Strings.AccountId, AccountId, (result) =>
			{
				AccountId = (BuildProviderAccountId)result;
				BuildMonitor.QueueSaveAppData();
				ShouldShowTokenPopup = true;
			});
			ShouldShowAccountIdPopup = false;
		}
		else if (ShouldShowTokenPopup)
		{
			PopupInputString.Open(Strings.SetToken, Strings.Token, string.Empty, (result) =>
			{
				Token = (BuildProviderToken)result;
				BuildMonitor.QueueSaveAppData();
			});
			ShouldShowTokenPopup = false;
		}
		else if (ShouldShowAddOwnerPopup)
		{
			PopupInputString.Open(Strings.AddOwner, Strings.OwnerName, string.Empty, (result) =>
			{
				var ownerName = (OwnerName)result;
				if (Owners.TryAdd(ownerName, CreateOwner(ownerName)))
				{
					BuildMonitor.QueueSaveAppData();
				}
			});
			ShouldShowAddOwnerPopup = false;
		}

		_ = PopupInputString.ShowIfOpen();
	}

	internal void OnAuthenticationFailure()
	{
		AccountId = new();
		Token = new();
		BuildMonitor.QueueSaveAppData();
		//ShouldShowAccountIdPopup = true;
		//ShouldShowTokenPopup = true;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
	protected void OnRateLimitExceeded() => RateLimitSleep += TimeSpan.FromMilliseconds(100);

	internal abstract Task UpdateRepositoriesAsync(Owner owner);
	internal abstract Task UpdateBuildsAsync(Repository repository);
	internal abstract Task UpdateBuildAsync(Build build);
	internal abstract Task UpdateRunAsync(Run run);
}
