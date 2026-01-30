// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;
using System.Text.Json.Serialization;

using Hexa.NET.ImGui;

using ktsu.ImGui.Popups;
using ktsu.Semantics.Strings;

/// <summary>
/// Represents the name of a build provider.
/// </summary>
public sealed record class BuildProviderName : SemanticString<BuildProviderName> { }
/// <summary>
/// Represents the account ID of a build provider.
/// </summary>
public sealed record class BuildProviderAccountId : SemanticString<BuildProviderAccountId> { }
/// <summary>
/// Represents the token of a build provider.
/// </summary>
public sealed record class BuildProviderToken : SemanticString<BuildProviderToken> { }

/// <summary>
/// Represents the operational status of a build provider.
/// </summary>
internal enum ProviderStatus
{
	/// <summary>Provider is operating normally.</summary>
	OK,
	/// <summary>Provider is rate limited.</summary>
	RateLimited,
	/// <summary>Provider authentication failed.</summary>
	AuthFailed,
	/// <summary>Provider encountered an error.</summary>
	Error
}

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

	/// <summary>
	/// Current operational status of the provider.
	/// </summary>
	[JsonIgnore]
	internal ProviderStatus Status { get; private set; } = ProviderStatus.OK;

	/// <summary>
	/// Timestamp when the status was last changed.
	/// </summary>
	[JsonIgnore]
	internal DateTimeOffset StatusTimestamp { get; private set; } = DateTimeOffset.UtcNow;

	/// <summary>
	/// Human-readable message describing the current status.
	/// </summary>
	[JsonIgnore]
	internal string StatusMessage { get; private set; } = string.Empty;

	/// <summary>
	/// When rate limited, the time when the rate limit resets.
	/// </summary>
	[JsonIgnore]
	internal DateTimeOffset? RateLimitResetTime { get; private set; }

	/// <summary>
	/// Controls the maximum number of concurrent requests to this provider.
	/// </summary>
	protected int MaxConcurrentRequests { get; set; } = 5;

	/// <summary>
	/// Semaphore to limit concurrent requests to this provider.
	/// </summary>
	internal SemaphoreSlim RequestSemaphore { get; } = new(5, 5);

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
				AccountId = result.As<BuildProviderAccountId>();
				BuildMonitor.QueueSaveAppData();
				ShouldShowTokenPopup = true;
			});
			ShouldShowAccountIdPopup = false;
		}
		else if (ShouldShowTokenPopup)
		{
			PopupInputString.Open(Strings.SetToken, Strings.Token, string.Empty, (result) =>
			{
				Token = result.As<BuildProviderToken>();
				BuildMonitor.QueueSaveAppData();
			});
			ShouldShowTokenPopup = false;
		}
		else if (ShouldShowAddOwnerPopup)
		{
			PopupInputString.Open(Strings.AddOwner, Strings.OwnerName, string.Empty, (result) =>
			{
				OwnerName ownerName = result.As<OwnerName>();
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
		SetStatus(ProviderStatus.AuthFailed, Strings.AuthFailedMessage);
		BuildMonitor.QueueSaveAppData();
	}

	protected void OnRateLimitExceeded(DateTimeOffset? resetTime = null)
	{
		RateLimitSleep += TimeSpan.FromMilliseconds(100);
		RateLimitResetTime = resetTime;
		string message = $"{Strings.RateLimitedMessage} {Strings.Delay}: {RateLimitSleep.TotalMilliseconds}ms";
		if (resetTime.HasValue)
		{
			message += $" {Strings.ResetsAt}: {resetTime.Value.ToLocalTime():HH:mm:ss}";
		}
		SetStatus(ProviderStatus.RateLimited, message);
	}

	/// <summary>
	/// Sets the provider status with a message.
	/// </summary>
	protected void SetStatus(ProviderStatus status, string message)
	{
		Status = status;
		StatusMessage = message;
		StatusTimestamp = DateTimeOffset.UtcNow;
	}

	/// <summary>
	/// Clears the provider status back to OK after a successful request.
	/// </summary>
	protected void ClearStatus()
	{
		if (Status != ProviderStatus.OK)
		{
			Status = ProviderStatus.OK;
			StatusMessage = string.Empty;
			StatusTimestamp = DateTimeOffset.UtcNow;
			RateLimitResetTime = null;
		}
	}

	internal void ClearData()
	{
		foreach ((OwnerName _, Owner owner) in Owners)
		{
			owner.Repositories.Clear();
		}
	}

	internal abstract Task UpdateRepositoriesAsync(Owner owner);
	internal abstract Task UpdateBuildsAsync(Repository repository);
	internal abstract Task UpdateBuildAsync(Build build);
	internal abstract Task UpdateRunAsync(Run run);
}
