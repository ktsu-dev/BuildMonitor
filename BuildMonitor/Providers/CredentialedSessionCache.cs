// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor;

/// <summary>
/// Holds the connection-backed session a provider talks to its API through, rebuilding it when the
/// credentials change and handing every caller a snapshot of its own.
/// </summary>
/// <remarks>
/// <para>
/// Provider updates run concurrently: <c>UpdateAsync</c> fans owners, repositories, builds and runs
/// out through <c>Task.WhenAll</c>, and each of those independently asks for the session. Two
/// things go wrong if that is left unsynchronized.
/// </para>
/// <para>
/// Two callers can both decide the session is stale and both build one, so one of the two
/// connections is overwritten without ever being disposed. And a caller that has already read the
/// session, then waits — inside a rate-limit delay, say — can find it replaced with null underneath
/// it by the time it dereferences, which is a <see cref="NullReferenceException"/> rather than the
/// "skipped, no client" path the caller checked for.
/// </para>
/// <para>
/// Both follow from re-reading shared fields, so this hands back the session as a value instead.
/// Callers hold what they were given for the whole of their request, and a replacement built behind
/// them does not disturb a request already in flight. The factory runs under the lock, so building
/// a connection is serialized against another caller building one — which is the point, since that
/// is what the duplicate work and the leak came from.
/// </para>
/// <para>
/// Holding a session is not enough on its own if a rebuild disposes it: the caller would keep a
/// reference to a closed connection and fail with <see cref="ObjectDisposedException"/>. So a caller
/// takes a <see cref="Lease"/> rather than the bare session, and a session that is replaced or
/// invalidated while leased is only retired. It is disposed when its last lease is released.
/// </para>
/// </remarks>
/// <typeparam name="TSession">The session type, which owns the connection and disposes it.</typeparam>
internal sealed class CredentialedSessionCache<TSession> : IDisposable
	where TSession : class, IDisposable
{
	private readonly Lock gate = new();
	private readonly Func<string, string, TSession> createSession;
	private Entry? current;
	private string? lastAccountId;
	private string? lastToken;
	private bool disposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="CredentialedSessionCache{TSession}"/> class.
	/// </summary>
	/// <param name="createSession">Builds a session from an account id and a token.</param>
	internal CredentialedSessionCache(Func<string, string, TSession> createSession) =>
		this.createSession = createSession;

	/// <summary>
	/// Gets the number of sessions built so far, which a test uses to show that concurrent callers
	/// share one rather than each building their own.
	/// </summary>
	internal int SessionsCreated { get; private set; }

	/// <summary>
	/// Leases the session for these credentials, building one if the cached session is missing or was
	/// built for different credentials.
	/// </summary>
	/// <param name="accountId">The account the session authenticates against.</param>
	/// <param name="token">The token the session authenticates with.</param>
	/// <returns>
	/// A lease on the session, to be held by the caller for the whole of its request and disposed when
	/// it is done. The session is not disposed while the lease is held, even if it is replaced.
	/// </returns>
	/// <exception cref="ObjectDisposedException">The cache has been disposed.</exception>
	internal Lease Get(string accountId, string token)
	{
		lock (gate)
		{
			ObjectDisposedException.ThrowIf(disposed, this);

			if (current is not null && lastAccountId == accountId && lastToken == token)
			{
				return new Lease(this, current);
			}

			// Drop the stale session first, so a factory that throws cannot leave credentials
			// recorded for a session that was never built.
			Invalidate();

			Entry created = new(createSession(accountId, token));
			current = created;
			lastAccountId = accountId;
			lastToken = token;
			SessionsCreated++;
			return new Lease(this, created);
		}
	}

	/// <summary>
	/// Retires the cached session and forgets the credentials it was built for, so the next caller
	/// builds a fresh one. The retired session is disposed now if nobody holds it, or when its last
	/// lease is released.
	/// </summary>
	internal void Invalidate()
	{
		lock (gate)
		{
			if (current is not null)
			{
				current.Retired = true;
				DisposeIfUnheld(current);
			}

			current = null;
			lastAccountId = null;
			lastToken = null;
		}
	}

	/// <summary>
	/// Retires the cached session, disposing it once nobody holds it.
	/// </summary>
	public void Dispose()
	{
		lock (gate)
		{
			if (disposed)
			{
				return;
			}

			Invalidate();
			disposed = true;
		}
	}

	private void Release(Entry entry)
	{
		lock (gate)
		{
			entry.Holders--;
			DisposeIfUnheld(entry);
		}
	}

	private static void DisposeIfUnheld(Entry entry)
	{
		if (entry.Retired && entry.Holders == 0)
		{
			entry.Session.Dispose();
		}
	}

	/// <summary>
	/// A session together with how many callers hold it, guarded by the cache's lock.
	/// </summary>
	internal sealed class Entry(TSession session)
	{
		internal TSession Session { get; } = session;

		internal int Holders { get; set; }

		internal bool Retired { get; set; }
	}

	/// <summary>
	/// A caller's hold on a session. The session stays undisposed until every lease on it is released,
	/// so a rebuild behind a caller cannot close the connection it is using.
	/// </summary>
	internal sealed class Lease : IDisposable
	{
		private readonly CredentialedSessionCache<TSession> owner;
		private readonly Entry entry;
		private int released;

		/// <summary>
		/// Initializes a new instance of the <see cref="Lease"/> class. Called under the cache's lock.
		/// </summary>
		/// <param name="owner">The cache the session came from.</param>
		/// <param name="entry">The session being leased.</param>
		internal Lease(CredentialedSessionCache<TSession> owner, Entry entry)
		{
			this.owner = owner;
			this.entry = entry;
			entry.Holders++;
		}

		/// <summary>
		/// Gets the leased session.
		/// </summary>
		internal TSession Session => entry.Session;

		/// <summary>
		/// Releases the lease. Releasing it again does nothing.
		/// </summary>
		public void Dispose()
		{
			if (Interlocked.Exchange(ref released, 1) == 0)
			{
				owner.Release(entry);
			}
		}
	}
}
