// -----------------------------------------------------------------------
// <copyright file="KvStoreCache.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using NATS.Client.KeyValueStore;

namespace AuthCallout.Nats.Identity;

/// <summary>
/// Caches <see cref="INatsKVStore"/> handles per bucket. <see cref="INatsKVContext.GetStoreAsync"/>
/// issues a <c>$JS.API.STREAM.INFO</c> round trip on every call, which on the auth hot path was a
/// full round trip per bucket per request. A handle is just the bucket name plus the stream info
/// captured at lookup, and every bucket looked up here is created by provisioning before any
/// request is served, so reusing it is safe. Call <see cref="Clear"/> after every (re)provision so
/// a config apply never keeps a stale handle.
/// </summary>
internal sealed class KvStoreCache(INatsKVContext kvContext)
{
    private readonly ConcurrentDictionary<string, Task<INatsKVStore>> _stores = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the cached handle for <paramref name="bucket"/>, looking it up once. The shared
    /// lookup deliberately runs with <see cref="CancellationToken.None"/>: it is reused by every
    /// concurrent request, so it must never be cancelled by whichever request happened to start it
    /// (a per-request timeout would otherwise poison the cache and surface as a cancellation in an
    /// unrelated request). Each caller applies its own token only to its own wait. A failed lookup
    /// is evicted so the next request retries against the server rather than reusing the failure.
    /// </summary>
    /// <param name="bucket">The physical bucket name.</param>
    /// <param name="cancellationToken">Cancellation for this caller's wait only.</param>
    /// <returns>The store handle.</returns>
    internal async Task<INatsKVStore> GetAsync(string bucket, CancellationToken cancellationToken)
    {
        var lookup = _stores.GetOrAdd(bucket, name => kvContext.GetStoreAsync(name, CancellationToken.None).AsTask());
        try
        {
            return await lookup.WaitAsync(cancellationToken);
        }
        catch (Exception) when (lookup.IsFaulted || lookup.IsCanceled)
        {
            _stores.TryRemove(new KeyValuePair<string, Task<INatsKVStore>>(bucket, lookup));
            throw;
        }
    }

    /// <summary>Drops every cached handle (call after provisioning).</summary>
    internal void Clear() => _stores.Clear();
}
