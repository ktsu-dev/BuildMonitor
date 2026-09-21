// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests for the synchronization half of the Azure DevOps client lifecycle.
/// </summary>
/// <remarks>
/// <c>AzureDevOps.EnsureAzureDevOpsClients</c> cannot be driven from a test: building a session
/// constructs a real <c>VssConnection</c> against dev.azure.com, and the update methods around it
/// need an ImGui application and live credentials. The part that went wrong is not any of that —
/// it is the caching and the handover, which <see cref="CredentialedSessionCache{TSession}"/> holds
/// on its own and which a fake session exercises exactly.
/// </remarks>
[TestClass]
public sealed class CredentialedSessionCacheTests
{
	/// <summary>
	/// Enough concurrent callers to reliably overlap. <c>UpdateAsync</c> fans out over every owner,
	/// repository, build and run at once, so this is not an unrealistic number of them.
	/// </summary>
	private const int ConcurrentCallers = 32;

	/// <summary>
	/// Repeats of each concurrent scenario. A race is probabilistic, so one round proves nothing;
	/// repeating narrows the chance of an unsynchronized cache slipping through to negligible.
	/// </summary>
	private const int Rounds = 40;

	private const string AccountId = "contoso";
	private const string Token = "pat-1";
	private const string RotatedToken = "pat-2";

	/// <summary>
	/// Stands in for the connection and clients an Azure DevOps session owns. The clients are what a
	/// caller dereferences after its rate-limit delay, so the fake tracks whether it is still usable
	/// at that point.
	/// </summary>
	private sealed class FakeSession : IDisposable
	{
		private int disposals;

		internal int Disposals => Volatile.Read(ref disposals);

		internal bool IsDisposed => Disposals > 0;

		internal string Credentials { get; }

		internal FakeSession(string credentials) => Credentials = credentials;

		public void Dispose() => Interlocked.Increment(ref disposals);
	}

	/// <summary>
	/// Builds a cache that records every session it hands out, widening the creation window so an
	/// unsynchronized cache has a fair chance to interleave rather than passing by luck.
	/// </summary>
	private static CredentialedSessionCache<FakeSession> CreateCache(ConcurrentBag<FakeSession> created) =>
		new((accountId, token) =>
		{
			Thread.Yield();
			FakeSession session = new($"{accountId}:{token}");
			created.Add(session);
			return session;
		});

	/// <summary>
	/// Releases every caller at once and collects what each one got back.
	/// </summary>
	private static List<T> RunConcurrently<T>(Func<int, T> callerBody)
	{
		ConcurrentBag<T> results = [];
		using Barrier barrier = new(ConcurrentCallers);

		Parallel.For(0, ConcurrentCallers, index =>
		{
			barrier.SignalAndWait();
			results.Add(callerBody(index));
		});

		return [.. results];
	}

	/// <summary>
	/// The leak half of the race. Callers arriving together on a cold cache must agree on one
	/// session: unsynchronized, several of them each see no session, each build one, and every
	/// connection but the last is overwritten without being disposed.
	/// </summary>
	[TestMethod]
	public void ConcurrentCallersOnAColdCacheShareOneSession()
	{
		for (int round = 0; round < Rounds; round++)
		{
			ConcurrentBag<FakeSession> created = [];
			using CredentialedSessionCache<FakeSession> cache = CreateCache(created);

			List<FakeSession> handedOut = RunConcurrently(_ => cache.Get(AccountId, Token));

			Assert.AreEqual(1, cache.SessionsCreated, $"round {round}: more than one session was built");
			Assert.AreEqual(1, created.Count, $"round {round}: more than one session was built");
			Assert.AreEqual(ConcurrentCallers, handedOut.Count);
			Assert.IsTrue(
				handedOut.TrueForAll(session => ReferenceEquals(session, handedOut[0])),
				$"round {round}: callers were handed different sessions");
		}
	}

	/// <summary>
	/// No session may be dropped without being disposed, which is the leaked <c>VssConnection</c> the
	/// issue names. Callers rotate the token underneath each other here, so sessions really are being
	/// replaced while others are asking for one.
	/// </summary>
	[TestMethod]
	public void EverySupersededSessionIsDisposedExactlyOnce()
	{
		for (int round = 0; round < Rounds; round++)
		{
			ConcurrentBag<FakeSession> created = [];
			CredentialedSessionCache<FakeSession> cache = CreateCache(created);

			List<FakeSession> handedOut = RunConcurrently(
				index => cache.Get(AccountId, index % 2 == 0 ? Token : RotatedToken));

			cache.Dispose();

			Assert.AreEqual(created.Count, cache.SessionsCreated);
			foreach (FakeSession session in created)
			{
				Assert.AreEqual(1, session.Disposals, $"round {round}: a session was disposed {session.Disposals} times");
			}

			Assert.AreEqual(ConcurrentCallers, handedOut.Count);
		}
	}

	/// <summary>
	/// The <see cref="NullReferenceException"/> half. A caller is handed its session before waiting
	/// out the rate-limit delay and dereferences the clients afterwards. A rebuild behind it used to
	/// null the shared fields in that window; holding the session as a value means the caller still
	/// has what it checked.
	/// </summary>
	[TestMethod]
	public void ASessionHandedToACallerSurvivesARebuildBehindIt()
	{
		for (int round = 0; round < Rounds; round++)
		{
			ConcurrentBag<FakeSession> created = [];
			using CredentialedSessionCache<FakeSession> cache = CreateCache(created);

			List<string> observed = RunConcurrently(index =>
			{
				// Half the callers rotate the credentials, standing in for a PAT change or a second
				// configured owner; the rest take a session and use it after a pause.
				string token = index % 2 == 0 ? Token : RotatedToken;
				FakeSession session = cache.Get(AccountId, token);
				Thread.Yield();
				return session.Credentials;
			});

			Assert.AreEqual(ConcurrentCallers, observed.Count);
			Assert.IsFalse(
				observed.Exists(string.IsNullOrEmpty),
				$"round {round}: a caller dereferenced a session it no longer held");
		}
	}

	/// <summary>
	/// Credentials that have not changed must not cause a rebuild, since that is the whole reason the
	/// session is cached rather than built per request.
	/// </summary>
	[TestMethod]
	public void UnchangedCredentialsReuseTheCachedSession()
	{
		ConcurrentBag<FakeSession> created = [];
		using CredentialedSessionCache<FakeSession> cache = CreateCache(created);

		FakeSession first = cache.Get(AccountId, Token);
		FakeSession second = cache.Get(AccountId, Token);

		Assert.AreSame(first, second);
		Assert.AreEqual(1, cache.SessionsCreated);
		Assert.IsFalse(first.IsDisposed);
	}

	/// <summary>
	/// A changed token rebuilds, and the session it replaces is disposed rather than dropped.
	/// </summary>
	[TestMethod]
	public void ChangedCredentialsRebuildAndDisposeThePreviousSession()
	{
		ConcurrentBag<FakeSession> created = [];
		using CredentialedSessionCache<FakeSession> cache = CreateCache(created);

		FakeSession first = cache.Get(AccountId, Token);
		FakeSession second = cache.Get(AccountId, RotatedToken);

		Assert.AreNotSame(first, second);
		Assert.AreEqual(2, cache.SessionsCreated);
		Assert.IsTrue(first.IsDisposed);
		Assert.IsFalse(second.IsDisposed);
	}

	/// <summary>
	/// A factory that throws — an unreachable organization, a malformed account name — must not leave
	/// the credentials recorded, or the cache would answer the next caller with a session it never
	/// built.
	/// </summary>
	[TestMethod]
	public void AFailedBuildLeavesNothingCachedForThoseCredentials()
	{
		int attempts = 0;
		using CredentialedSessionCache<FakeSession> cache = new((accountId, token) =>
			++attempts == 1
				? throw new InvalidOperationException("connection refused")
				: new FakeSession($"{accountId}:{token}"));

		_ = Assert.ThrowsExactly<InvalidOperationException>(() => cache.Get(AccountId, Token));

		FakeSession recovered = cache.Get(AccountId, Token);

		Assert.AreEqual(2, attempts);
		Assert.AreEqual($"{AccountId}:{Token}", recovered.Credentials);
	}

	/// <summary>
	/// Invalidation disposes what is cached and forces the next caller to build, which is what a
	/// caller does after an authentication failure.
	/// </summary>
	[TestMethod]
	public void InvalidateDisposesTheSessionAndForcesARebuild()
	{
		ConcurrentBag<FakeSession> created = [];
		using CredentialedSessionCache<FakeSession> cache = CreateCache(created);

		FakeSession first = cache.Get(AccountId, Token);
		cache.Invalidate();
		FakeSession second = cache.Get(AccountId, Token);

		Assert.IsTrue(first.IsDisposed);
		Assert.AreNotSame(first, second);
		Assert.AreEqual(2, cache.SessionsCreated);
	}

	/// <summary>
	/// Disposing the cache disposes the connection it holds, and is safe to repeat.
	/// </summary>
	[TestMethod]
	public void DisposeDisposesTheCachedSessionOnce()
	{
		ConcurrentBag<FakeSession> created = [];
		CredentialedSessionCache<FakeSession> cache = CreateCache(created);
		FakeSession session = cache.Get(AccountId, Token);

		cache.Dispose();
		cache.Dispose();

		Assert.AreEqual(1, session.Disposals);
	}

	/// <summary>
	/// Asking a disposed cache for a session is a programming error rather than a silently missing
	/// client.
	/// </summary>
	[TestMethod]
	public void GetAfterDisposeThrows()
	{
		ConcurrentBag<FakeSession> created = [];
		CredentialedSessionCache<FakeSession> cache = CreateCache(created);
		cache.Dispose();

		_ = Assert.ThrowsExactly<ObjectDisposedException>(() => cache.Get(AccountId, Token));
	}
}
