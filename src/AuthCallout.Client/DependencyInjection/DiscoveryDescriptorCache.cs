// -----------------------------------------------------------------------
// <copyright file="DiscoveryDescriptorCache.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Identity;
using NATS.Client.KeyValueStore;

namespace AuthCallout.Client.DependencyInjection;

/// <summary>
/// A per-application cache over <see cref="CalloutDiscovery.FetchAsync"/> (doc 06: discovery is
/// fetched once per connection and cached by both client and subscriber). Registered as a
/// singleton by <c>ServiceCollectionExtensions.AddCalloutNatsClient</c> so repeated resolution
/// doesn't re-fetch the descriptor.
/// </summary>
public sealed class DiscoveryDescriptorCache : IDisposable
{
    private readonly Dictionary<string, Task<DiscoveryDescriptor>> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Gets the cached descriptor for <paramref name="appName"/>, fetching it on a cache miss.</summary>
    /// <param name="kv">The connected client's KV context.</param>
    /// <param name="appName">The application's authored name.</param>
    /// <param name="cancellationToken">Cancellation for the fetch, on a cache miss.</param>
    /// <returns>The cached or freshly-fetched descriptor.</returns>
    public async Task<DiscoveryDescriptor> GetOrFetchAsync(INatsKVContext kv, string appName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kv);
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        if (_cache.TryGetValue(appName, out var cached))
        {
            return await cached.ConfigureAwait(false);
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_cache.TryGetValue(appName, out cached))
            {
                cached = CalloutDiscovery.FetchAsync(kv, appName, cancellationToken);
                _cache[appName] = cached;
            }
        }
        finally
        {
            _lock.Release();
        }

        return await cached.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();
}
