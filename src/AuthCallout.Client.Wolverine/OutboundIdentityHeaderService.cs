// -----------------------------------------------------------------------
// <copyright file="OutboundIdentityHeaderService.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using AuthCallout.Client.Identity;
using NATS.Net;

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// Builds, lazily on first use and cached thereafter (ADR 0022 task 2: "The assertion fetch...
/// runs lazily on first send and is cached for the life of the connection"), the
/// <c>Callout-Identity</c> header value this host attaches to outgoing Wolverine envelopes, and
/// doubles as the <see cref="ICalloutIdentityVerifierAccessor"/> for the inbound middleware.
/// Both run over the Wolverine transport's own connection
/// <see cref="CalloutTransportContext.Connection"/>), whose scoped inbox prefix
/// (<see cref="CalloutPrefixPrewarmService"/>) authorizes the KV request/reply traffic.
/// </summary>
public sealed class OutboundIdentityHeaderService : ICalloutIdentityVerifierAccessor, IDisposable
{
    private readonly CalloutWolverineOptions _options;
    private readonly CalloutTransportContext _transportContext;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _buildLock = new(1, 1);

    private CalloutIdentityResolver? _resolver;
    private CalloutIdentityHeader? _header;
    private CalloutIdentityVerifier? _verifier;
    private string? _cachedHeaderValue;

    /// <summary>Initializes a new instance of the <see cref="OutboundIdentityHeaderService"/> class.</summary>
    /// <param name="options">The adapter options (application name + token provider).</param>
    /// <param name="transportContext">The transport context exposing the Wolverine connection.</param>
    /// <param name="timeProvider">The time provider for assertion expiry checks; defaults to <see cref="TimeProvider.System"/>.</param>
    public OutboundIdentityHeaderService(CalloutWolverineOptions options, CalloutTransportContext transportContext, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transportContext);
        _options = options;
        _transportContext = transportContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<ICalloutIdentityVerifier> GetVerifierAsync(CancellationToken cancellationToken = default)
    {
        await EnsureBuiltAsync(cancellationToken).ConfigureAwait(false);
        return _verifier!;
    }

    /// <summary>
    /// Pre-fetches and caches the identity header's name and value (ADR 0022 task 2: "cached for
    /// the life of the connection"), called once by <see cref="CalloutHostedConnection"/> after
    /// Wolverine's transport has connected, so <see cref="TryGetCachedHeaderValue"/> always has
    /// an answer before <see cref="CalloutIdentityOutboundRule"/>'s synchronous <c>Modify</c>
    /// ever needs it (a Wolverine <c>IEnvelopeRule</c> has no async hook to fetch it lazily per
    /// send). A no-op (<see cref="TryGetCachedHeaderValue"/> then always returns null) when the
    /// application's discovery descriptor has identity propagation disabled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the discovery/self-assertion fetch.</param>
    /// <returns>A task that completes once the header is cached.</returns>
    public async Task PrefetchAsync(CancellationToken cancellationToken = default)
    {
        await EnsureBuiltAsync(cancellationToken).ConfigureAwait(false);
        var headers = await _header!.AttachAsync(null, cancellationToken).ConfigureAwait(false);

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

    /// <inheritdoc />
    public void Dispose()
    {
        _buildLock.Dispose();
        _header?.Dispose();
        _resolver?.Dispose();
    }

    private async Task EnsureBuiltAsync(CancellationToken cancellationToken)
    {
        if (_verifier is not null && _header is not null)
        {
            return;
        }

        await _buildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_verifier is null || _header is null)
            {
                var connection = _transportContext.Connection;
                var kv = connection.CreateKeyValueStoreContext();
                var descriptor = await CalloutDiscovery.FetchAsync(kv, _options.ApplicationName, cancellationToken).ConfigureAwait(false);

                // Must be the token the transport actually presented, not a fresh fetch: the
                // self-assertion is keyed by the presented token's jti.
                var presentedToken = _transportContext.PresentedToken
                    ?? throw new CalloutAuthenticationRequiredException();

                _header = new CalloutIdentityHeader(kv, descriptor, presentedToken);
                _resolver = new CalloutIdentityResolver(kv, descriptor, _timeProvider);
                _verifier = new CalloutIdentityVerifier(_resolver);
            }
        }
        finally
        {
            _buildLock.Release();
        }
    }
}
