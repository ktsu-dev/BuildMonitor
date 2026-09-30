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

	/// <summary>
	/// Runs <paramref name="update"/> for every item concurrently, each guarded by
	/// <see cref="RunAsync"/>, so one item that throws is logged and the rest still run.
	/// </summary>
	/// <typeparam name="T">The type of item being updated.</typeparam>
	/// <param name="items">The items to update.</param>
	/// <param name="update">The update to run for each item.</param>
	/// <param name="describe">Describes an item for the log line when its update fails.</param>
	/// <returns>A task that completes, without faulting, once every update has finished.</returns>
	internal static Task RunAllAsync<T>(IEnumerable<T> items, Func<T, Task> update, Func<T, string> describe)
	{
		Ensure.NotNull(items);
		Ensure.NotNull(update);
		Ensure.NotNull(describe);

		return Task.WhenAll(items.Select(item => RunAsync(() => update(item), describe(item))));
	}
}
