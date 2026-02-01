// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.BuildMonitor;

using System.Collections.Concurrent;

/// <summary>
/// Represents the severity level of a log entry.
/// </summary>
internal enum LogLevel
{
	/// <summary>Debug level for verbose information.</summary>
	Debug,
	/// <summary>Info level for general information.</summary>
	Info,
	/// <summary>Warning level for potential issues.</summary>
	Warning,
	/// <summary>Error level for errors and failures.</summary>
	Error
}

/// <summary>
/// Represents a single log entry.
/// </summary>
internal sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message);

/// <summary>
/// Thread-safe logging system for the application.
/// </summary>
internal static class Log
{
	private static ConcurrentQueue<LogEntry> Entries { get; } = new();
	private const int MaxEntries = 1000;

	/// <summary>
	/// Gets all log entries in chronological order.
	/// </summary>
	internal static IEnumerable<LogEntry> GetEntries() => [.. Entries];

	/// <summary>
	/// Logs a debug message.
	/// </summary>
	internal static void Debug(string message) => AddEntry(LogLevel.Debug, message);

	/// <summary>
	/// Logs an info message.
	/// </summary>
	internal static void Info(string message) => AddEntry(LogLevel.Info, message);

	/// <summary>
	/// Logs a warning message.
	/// </summary>
	internal static void Warning(string message) => AddEntry(LogLevel.Warning, message);

	/// <summary>
	/// Logs an error message.
	/// </summary>
	internal static void Error(string message) => AddEntry(LogLevel.Error, message);

	private static void AddEntry(LogLevel level, string message)
	{
		Entries.Enqueue(new LogEntry(DateTimeOffset.Now, level, message));

		// Trim old entries if we exceed the max
		while (Entries.Count > MaxEntries)
		{
			_ = Entries.TryDequeue(out _);
		}
	}

	/// <summary>
	/// Clears all log entries.
	/// </summary>
	internal static void Clear()
	{
		while (Entries.TryDequeue(out _))
		{
		}
	}
}
