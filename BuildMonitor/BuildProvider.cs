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
	/// <summary>
	/// Base delay between requests when not rate limited.
	/// </summary>
	protected static TimeSpan BaseRequestDelay { get; } = TimeSpan.FromMilliseconds(500);

	/// <summary>
	/// Current delay between requests. Increases during rate limiting and resets after recovery.
	/// </summary>
	protected TimeSpan RateLimitSleep { get; set; } = BaseRequestDelay;

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
	/// Remaining requests in the current rate limit window (from successful responses).
	/// </summary>
	[JsonIgnore]
	protected int? RateLimitBudgetRemaining { get; private set; }

	/// <summary>
	/// Total rate limit for the current window (from successful responses).
	/// </summary>
	[JsonIgnore]
	protected int? RateLimitBudgetLimit { get; private set; }

	/// <summary>
	/// When the current rate limit window resets (from successful responses).
	/// </summary>
	[JsonIgnore]
	protected DateTimeOffset? RateLimitBudgetResetTime { get; private set; }

	/// <summary>
	/// Gets a display string showing the current rate limit consumption (e.g., "4532/5000").
	/// Returns null if rate limit info is not available.
	/// </summary>
	[JsonIgnore]
	internal string? RateLimitDisplay
	{
		get
		{
			if (!RateLimitBudgetRemaining.HasValue || !RateLimitBudgetLimit.HasValue)
			{
				return null;
			}

			return $"{RateLimitBudgetRemaining.Value}/{RateLimitBudgetLimit.Value}";
		}
	}

	/// <summary>
	/// Gets a detailed rate limit status string including reset time.
	/// </summary>
	[JsonIgnore]
	internal string? RateLimitDetailedStatus
	{
		get
		{
			if (!RateLimitBudgetRemaining.HasValue || !RateLimitBudgetLimit.HasValue)
			{
				return null;
			}

			string status = $"{Strings.RateLimitBudget}: {RateLimitBudgetRemaining.Value}/{RateLimitBudgetLimit.Value}";

			if (RateLimitBudgetResetTime.HasValue)
			{
				TimeSpan timeUntilReset = RateLimitBudgetResetTime.Value - DateTimeOffset.UtcNow;
				if (timeUntilReset > TimeSpan.Zero)
				{
					status += $" ({Strings.ResetsIn} {FormatTimeSpan(timeUntilReset)})";
				}
			}

			return status;
		}
	}

	/// <summary>
	/// Minimum delay between requests even when budget is plentiful.
	/// </summary>
	protected static TimeSpan MinRequestDelay { get; } = TimeSpan.FromMilliseconds(100);

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
		RateLimitResetTime = resetTime;

		string message;
		if (resetTime.HasValue)
		{
			// Calculate time until reset with a small buffer (5 seconds) for clock skew
			TimeSpan timeUntilReset = resetTime.Value - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
			if (timeUntilReset > TimeSpan.Zero)
			{
				message = $"{Strings.RateLimitedMessage} {Strings.ResetsAt}: {resetTime.Value.ToLocalTime():HH:mm:ss} ({Strings.WaitingFor} {FormatTimeSpan(timeUntilReset)})";
			}
			else
			{
				// Reset time already passed, use minimal delay
				message = $"{Strings.RateLimitedMessage} {Strings.ResetsAt}: {resetTime.Value.ToLocalTime():HH:mm:ss} ({Strings.ResetImminent})";
			}
		}
		else
		{
			// No reset time available, use incremental backoff
			RateLimitSleep += TimeSpan.FromMilliseconds(500);
			message = $"{Strings.RateLimitedMessage} {Strings.Delay}: {RateLimitSleep.TotalMilliseconds}ms";
		}
		SetStatus(ProviderStatus.RateLimited, message);
	}

	/// <summary>
	/// Updates the rate limit budget information from a successful API response.
	/// This enables pre-emptive pacing to avoid hitting rate limits.
	/// </summary>
	/// <param name="remaining">Remaining requests in the current window.</param>
	/// <param name="limit">Total requests allowed in the window.</param>
	/// <param name="resetTime">When the rate limit window resets.</param>
	protected void UpdateRateLimitBudget(int remaining, int limit, DateTimeOffset resetTime)
	{
		RateLimitBudgetRemaining = remaining;
		RateLimitBudgetLimit = limit;
		RateLimitBudgetResetTime = resetTime;
	}

	/// <summary>
	/// Gets the appropriate wait time before making the next request.
	/// Uses adaptive pacing based on remaining budget, or waits for reset if rate limited.
	/// </summary>
	/// <returns>The time to wait before the next request.</returns>
	protected TimeSpan GetRateLimitWaitTime()
	{
		// If fully rate limited with a known reset time, wait until reset
		if (Status == ProviderStatus.RateLimited && RateLimitResetTime.HasValue)
		{
			// Calculate time until reset with a small buffer (5 seconds) for clock skew
			TimeSpan timeUntilReset = RateLimitResetTime.Value - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);

			if (timeUntilReset > TimeSpan.Zero)
			{
				// Cap the wait time at 10 minutes to avoid extremely long waits
				TimeSpan maxWait = TimeSpan.FromMinutes(10);
				return timeUntilReset > maxWait ? maxWait : timeUntilReset;
			}
		}

		// Use adaptive pacing based on remaining budget
		TimeSpan pacedDelay = CalculateAdaptivePacing();
		return pacedDelay > RateLimitSleep ? pacedDelay : RateLimitSleep;
	}

	/// <summary>
	/// Calculates an adaptive delay based on remaining rate limit budget.
	/// Spreads requests evenly across the remaining time window.
	/// </summary>
	private TimeSpan CalculateAdaptivePacing()
	{
		// Need budget info to calculate pacing
		if (!RateLimitBudgetRemaining.HasValue || !RateLimitBudgetResetTime.HasValue)
		{
			return BaseRequestDelay;
		}

		int remaining = RateLimitBudgetRemaining.Value;
		DateTimeOffset resetTime = RateLimitBudgetResetTime.Value;
		TimeSpan timeUntilReset = resetTime - DateTimeOffset.UtcNow;

		// If reset time has passed or is imminent, use minimum delay
		if (timeUntilReset <= TimeSpan.Zero)
		{
			return MinRequestDelay;
		}

		// If we have no remaining requests, we should be rate limited
		if (remaining <= 0)
		{
			return timeUntilReset;
		}

		// Reserve some budget for unexpected requests (keep 10% or at least 50 requests)
		int reservedBudget = Math.Max(50, (RateLimitBudgetLimit ?? 5000) / 10);
		int usableBudget = Math.Max(1, remaining - reservedBudget);

		// Calculate delay to spread remaining budget across the time window
		// Add a small multiplier (1.1) to be slightly conservative
		double delayMs = timeUntilReset.TotalMilliseconds / usableBudget * 1.1;

		// Clamp between minimum delay and a reasonable maximum (30 seconds)
		delayMs = Math.Clamp(delayMs, MinRequestDelay.TotalMilliseconds, 30000);

		return TimeSpan.FromMilliseconds(delayMs);
	}

	/// <summary>
	/// Checks if we are currently rate limited and should skip making requests.
	/// Returns true if rate limited and reset time is in the future.
	/// </summary>
	protected bool IsRateLimitedWithPendingReset()
	{
		if (Status != ProviderStatus.RateLimited || !RateLimitResetTime.HasValue)
		{
			return false;
		}

		return RateLimitResetTime.Value > DateTimeOffset.UtcNow;
	}

	private static string FormatTimeSpan(TimeSpan timeSpan)
	{
		if (timeSpan.TotalHours >= 1)
		{
			return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
		}
		if (timeSpan.TotalMinutes >= 1)
		{
			return $"{(int)timeSpan.TotalMinutes}m {timeSpan.Seconds}s";
		}
		return $"{(int)timeSpan.TotalSeconds}s";
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
	/// Resets rate limit sleep time back to base value if recovering from rate limiting.
	/// </summary>
	protected void ClearStatus()
	{
		if (Status != ProviderStatus.OK)
		{
			// Reset rate limit sleep back to base value when recovering from rate limiting
			if (Status == ProviderStatus.RateLimited)
			{
				RateLimitSleep = BaseRequestDelay;
			}

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
