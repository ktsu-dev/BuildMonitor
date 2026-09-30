// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor;

/// <summary>
/// Runs one item of a batched provider update so that its failure is logged instead of faulting
/// the whole batch.
/// </summary>
/// <remarks>
/// The update loop awaits each batch with <see cref="Task.WhenAll(IEnumerable{Task})"/>. A single
/// exception — a persistent 5xx from one repository, say — used to fault that batch, which skipped
/// the timer restarts and pruning after it and made the loop start over straight away instead of on
/// its interval (ktsu-dev/BuildMonitor#299).
/// </remarks>
internal static class SyncGuard
{
	/// <summary>
	/// Runs <paramref name="update"/>, logging any exception it throws rather than propagating it.
	/// </summary>
	/// <param name="update">The update to run.</param>
	/// <param name="description">What is being updated, for the log line.</param>
	/// <returns>True when the update completed, false when it threw.</returns>
	internal static async Task<bool> RunAsync(Func<Task> update, string description)
	{
		Ensure.NotNull(update);

		try
		{
			await update().ConfigureAwait(false);
			return true;
		}
#pragma warning disable CA1031 // Do not catch general exception types - one failing item must not stop the rest of the batch
		catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
		{
			Log.Error($"{description} failed: {ex.Message}");
			return false;
		}
	}
}
