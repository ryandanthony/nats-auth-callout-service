// -----------------------------------------------------------------------
// <copyright file="OutboundIdentityHeaderService.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using AuthCallout.Client.DependencyInjection;
using AuthCallout.Client.Identity;
using AuthCallout.Client.Tokens;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Net;

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// Builds, lazily on first use and cached thereafter (ADR 0022 task 2: "The assertion fetch...
/// runs lazily on first send and is cached for the life of the connection"), the
/// <c>Callout-Identity</c> header value this host attaches to outgoing Wolverine envelopes, and
/// doubles as the <see cref="ICalloutIdentityVerifierAccessor"/> for the inbound middleware
/// (both need the same discovery descriptor + KV connection
/// <see cref="CalloutHostedConnection"/> establishes at startup, via
/// <see cref="CalloutIdentityServices"/>).
/// </summary>
public sealed class OutboundIdentityHeaderService : ICalloutIdentityVerifierAccessor, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private CalloutIdentityHeader? _header;
    private string? _cachedHeaderValue;

    /// <summary>Initializes a new instance of the <see cref="OutboundIdentityHeaderService"/> class.</summary>
    /// <param name="serviceProvider">The container to resolve <see cref="CalloutClientOptions"/>, <see cref="CalloutNatsConnection"/>, <see cref="DiscoveryDescriptorCache"/>, and <see cref="CalloutIdentityServices"/> from.</param>
    public OutboundIdentityHeaderService(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public Task<ICalloutIdentityVerifier> GetVerifierAsync(CancellationToken cancellationToken = default) =>
        _serviceProvider.GetRequiredService<CalloutIdentityServices>().GetIdentityVerifierAsync(cancellationToken);

    /// <summary>
    /// Pre-fetches and caches the identity header's name and value (ADR 0022 task 2: "cached for
    /// the life of the connection... refreshed after reconnect"), called once by
    /// <see cref="CalloutHostedConnection"/> at startup so <see cref="TryGetCachedHeaderValue"/>
    /// always has an answer before <see cref="CalloutIdentityOutboundRule"/>'s synchronous
    /// <c>Modify</c> ever needs it (a Wolverine <c>IEnvelopeRule</c> has no async hook to fetch
    /// it lazily per send). A no-op (<see cref="TryGetCachedHeaderValue"/> then always returns
    /// null) when the application's discovery descriptor has identity propagation disabled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the discovery/self-assertion fetch.</param>
    /// <returns>A task that completes once the header is cached.</returns>
    public async Task PrefetchAsync(CancellationToken cancellationToken = default)
    {
        var header = await GetOrBuildHeaderAsync(cancellationToken).ConfigureAwait(false);
        var headers = await header.AttachAsync(null, cancellationToken).ConfigureAwait(false);

        // AttachAsync sets exactly one header (the descriptor's configured name, or
        // CalloutIdentityHeader.FallbackName) when propagation is enabled, or returns null when
        // it is disabled; there is never more than one key to read.
        var headerName = headers?.Keys.SingleOrDefault();
        _cachedHeaderValue = headerName is null ? null : headers![headerName].ToString();
    }

    /// <summary>
    /// Synchronously returns the cached <c>Callout-Identity</c> header value prefetched by
    /// <see cref="PrefetchAsync"/>, or null if propagation is disabled for this application or
    /// prefetching has not run yet.
    /// </summary>
    /// <returns>The cached header value, or null.</returns>
    public string? TryGetCachedHeaderValue() => _cachedHeaderValue;

    /// <summary>
    /// Test-only seam: sets the cached header value directly, bypassing <see cref="PrefetchAsync"/>'s
    /// KV/discovery fetch, so <see cref="CalloutIdentityOutboundRule"/> can be tested in
    /// isolation from a live callout connection.
    /// </summary>
    /// <param name="headerValue">The value <see cref="TryGetCachedHeaderValue"/> should return, or null.</param>
    internal void SetCachedHeaderValueForTests(string? headerValue) => _cachedHeaderValue = headerValue;

    /// <summary>
    /// Attaches the cached (fetching once if needed) <c>Callout-Identity</c> header to
    /// <paramref name="headers"/>, or does nothing when identity propagation is disabled for
    /// this application (ADR 0022 task 2: "no-op when the descriptor says <c>enabled: false</c>").
    /// </summary>
    /// <param name="headers">The headers to attach to; mutated in place. Created if null and propagation is enabled.</param>
    /// <param name="cancellationToken">Cancellation for the self-assertion fetch, if needed.</param>
    /// <returns>The headers to publish with (either <paramref name="headers"/> or a new instance), or null if propagation is disabled and none were supplied.</returns>
    public async Task<NatsHeaders?> AttachAsync(NatsHeaders? headers, CancellationToken cancellationToken = default)
    {
        var header = await GetOrBuildHeaderAsync(cancellationToken).ConfigureAwait(false);
        return await header.AttachAsync(headers, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _buildLock.Dispose();
        _header?.Dispose();
    }

    private async Task<CalloutIdentityHeader> GetOrBuildHeaderAsync(CancellationToken cancellationToken)
    {
        if (_header is not null)
        {
            return _header;
        }

        await _buildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_header is null)
            {
                var options = _serviceProvider.GetRequiredService<CalloutClientOptions>();
                var managed = _serviceProvider.GetRequiredService<CalloutNatsConnection>();
                var connection = managed.Connection
                    ?? throw new InvalidOperationException("The callout KV connection has not connected yet; this runs after CalloutHostedConnection.StartAsync.");

                var kv = ((INatsConnection)connection).CreateKeyValueStoreContext();
                var cache = _serviceProvider.GetRequiredService<DiscoveryDescriptorCache>();
                var descriptor = await cache.GetOrFetchAsync(kv, options.ApplicationName, cancellationToken).ConfigureAwait(false);

                var tokenProvider = _serviceProvider.GetRequiredService<ICalloutTokenProvider>();
                var presentedToken = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new CalloutAuthenticationRequiredException();

                _header = new CalloutIdentityHeader(kv, descriptor, presentedToken);
            }

            return _header;
        }
        finally
        {
            _buildLock.Release();
        }
    }
}
