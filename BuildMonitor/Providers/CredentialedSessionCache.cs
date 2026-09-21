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
/// </remarks>
/// <typeparam name="TSession">The session type, which owns the connection and disposes it.</typeparam>
internal sealed class CredentialedSessionCache<TSession> : IDisposable
	where TSession : class, IDisposable
{
	private readonly Lock gate = new();
	private readonly Func<string, string, TSession> createSession;
	private TSession? session;
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
	/// Gets the session for these credentials, building one if the cached session is missing or was
	/// built for different credentials.
	/// </summary>
	/// <param name="accountId">The account the session authenticates against.</param>
	/// <param name="token">The token the session authenticates with.</param>
	/// <returns>The session, to be held by the caller for the whole of its request.</returns>
	/// <exception cref="ObjectDisposedException">The cache has been disposed.</exception>
	internal TSession Get(string accountId, string token)
	{
		lock (gate)
		{
			ObjectDisposedException.ThrowIf(disposed, this);

			if (session is not null && lastAccountId == accountId && lastToken == token)
			{
				return session;
			}

			// Drop the stale session first, so a factory that throws cannot leave credentials
			// recorded for a session that was never built.
			Invalidate();

			TSession created = createSession(accountId, token);
			session = created;
			lastAccountId = accountId;
			lastToken = token;
			SessionsCreated++;
			return created;
		}
	}

	/// <summary>
	/// Disposes the cached session and forgets the credentials it was built for, so the next caller
	/// builds a fresh one.
	/// </summary>
	internal void Invalidate()
	{
		lock (gate)
		{
			session?.Dispose();
			session = null;
			lastAccountId = null;
			lastToken = null;
		}
	}

	/// <summary>
	/// Disposes the cached session.
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
}
