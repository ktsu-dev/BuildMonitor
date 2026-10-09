// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor;

/// <summary>
/// The wall clock, for every time that reaches the screen: how long a run has taken, how long it has
/// left, when it started, and when a log entry was written.
/// </summary>
/// <remarks>
/// With nothing overridden this reads exactly what <see cref="DateTimeOffset.UtcNow"/>,
/// <see cref="DateTimeOffset.Now"/> and <see cref="DateTimeOffset.ToLocalTime"/> would, so the running
/// application is unchanged. The UI tests pin both the instant and the time zone, which is what lets a
/// picture of the build table come out identical from one run to the next and from one machine to
/// another.
/// </remarks>
internal static class Clock
{
	/// <summary>Gets or sets the source of the current instant, or <see langword="null"/> for the system clock.</summary>
	internal static Func<DateTimeOffset>? UtcNowOverride { get; set; }

	/// <summary>Gets or sets the time zone local times are shown in, or <see langword="null"/> for the machine's own.</summary>
	internal static TimeZoneInfo? LocalZoneOverride { get; set; }

	/// <summary>Gets the current instant.</summary>
	internal static DateTimeOffset UtcNow => UtcNowOverride?.Invoke() ?? DateTimeOffset.UtcNow;

	/// <summary>Gets the current instant in the local time zone.</summary>
	internal static DateTimeOffset Now => ToLocal(UtcNow);

	/// <summary>Converts an instant to the local time zone.</summary>
	/// <param name="value">The instant to convert.</param>
	/// <returns>The same instant, offset for the local time zone.</returns>
	internal static DateTimeOffset ToLocal(DateTimeOffset value) =>
		LocalZoneOverride is TimeZoneInfo zone ? TimeZoneInfo.ConvertTime(value, zone) : value.ToLocalTime();

	/// <summary>Goes back to the system clock and the machine's time zone.</summary>
	internal static void Reset()
	{
		UtcNowOverride = null;
		LocalZoneOverride = null;
	}
}
