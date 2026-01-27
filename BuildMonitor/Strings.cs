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
}
