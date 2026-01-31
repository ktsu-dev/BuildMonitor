// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using Humanizer;

internal static class Strings
{
	internal static string FullyQualifiedApplicationName { get; } = $"{nameof(ktsu)}.{nameof(BuildMonitor)}";
	internal static string ApplicationName { get; } = nameof(BuildMonitor).Titleize();
	internal static string File { get; } = nameof(File).Titleize();
	internal static string ClearData { get; } = nameof(ClearData).Titleize();
	internal static string Exit { get; } = nameof(Exit).Titleize();
	internal static string Settings { get; } = nameof(Settings).Titleize();
	internal static string Providers { get; } = nameof(Providers).Titleize();
	internal static string SetCredentials { get; } = nameof(SetCredentials).Titleize();
	internal static string SetAccountId { get; } = nameof(SetAccountId).Titleize();
	internal static string AccountId { get; } = nameof(AccountId).Titleize();
	internal static string SetToken { get; } = nameof(SetToken).Titleize();
	internal static string Token { get; } = nameof(Token).Titleize();
	internal static string Set { get; } = nameof(Set).Titleize();
	internal static string AddOwner { get; } = nameof(AddOwner).Titleize();
	internal static string OwnerName { get; } = nameof(OwnerName).Titleize();
	internal static string Builds { get; } = nameof(Builds).Titleize();
	internal static string Repository { get; } = nameof(Repository).Titleize();
	internal static string BuildName { get; } = nameof(BuildName).Titleize();
	internal static string History { get; } = nameof(History).Titleize();
	internal static string Progress { get; } = nameof(Progress).Titleize();
	internal static string ETA { get; } = nameof(ETA).Titleize();
	internal static string Status { get; } = nameof(Status).Titleize();
	internal static string Duration { get; } = nameof(Duration).Titleize();
	internal static string Branch { get; } = nameof(Branch).Titleize();
	internal static string LastRun { get; } = nameof(LastRun).Titleize();
	internal static string Errors { get; } = nameof(Errors).Titleize();
	internal static string ErrorDetails { get; } = nameof(ErrorDetails).Titleize();
	internal static string OK { get; } = nameof(OK).Titleize();
	internal static string ProviderStatus { get; } = nameof(ProviderStatus).Titleize();
	internal static string RateLimited { get; } = nameof(RateLimited).Titleize();
	internal static string RateLimitedMessage { get; } = "Rate limited.";
	internal static string AuthFailed { get; } = nameof(AuthFailed).Titleize();
	internal static string AuthFailedMessage { get; } = "Authentication failed. Please update credentials.";
	internal static string ConnectionError { get; } = nameof(ConnectionError).Titleize();
	internal static string ConnectionErrorMessage { get; } = "Connection error.";
	internal static string Delay { get; } = nameof(Delay).Titleize();
	internal static string ResetsAt { get; } = "Resets at";
	internal static string WaitingFor { get; } = "waiting";
	internal static string ResetImminent { get; } = "reset imminent";
	internal static string RateLimitBudget { get; } = "API Budget";
	internal static string ResetsIn { get; } = "resets in";
}
