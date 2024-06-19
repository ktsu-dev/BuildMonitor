namespace ktsu.io.BuildMonitor;

using System.Text.Json.Serialization;
using ImGuiNET;
using ktsu.io.ImGuiWidgets;
using ktsu.io.StrongStrings;

public sealed record class BuildProviderName : StrongStringAbstract<BuildProviderName> { }
public sealed record class BuildProviderAccountId : StrongStringAbstract<BuildProviderAccountId> { }
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
	internal Dictionary<OwnerName, Owner> Owners { get; init; } = [];
	private bool ShouldShowAccountIdPopup { get; set; }
	private bool ShouldShowTokenPopup { get; set; }
	private bool ShouldShowAddOwnerPopup { get; set; }
	private PopupInputString PopupInputString { get; } = new();

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

		if (ShouldShowTokenPopup)
		{
			PopupInputString.Open(Strings.SetToken, Strings.Token, string.Empty, (result) =>
			{
				Token = (BuildProviderToken)result;
				BuildMonitor.QueueSaveAppData();
			});
			ShouldShowTokenPopup = false;
		}

		if (ShouldShowAddOwnerPopup)
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

	internal abstract void OnTick();
	internal abstract Task SyncRepositoriesAsync(Owner owner);
	internal abstract Task SyncBuildsAsync(Repository repository);
	internal abstract Task SyncRunsAsync(Build build);
	internal abstract Task SyncRunsAsync(Repository repository);
}
