// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityHeader.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NATS.Client.Core;
using NATS.Client.KeyValueStore;

namespace AuthCallout.Client.Identity;

/// <summary>
/// The client-side identity-attach helper (doc 06: "Client discovers behavior, then fetches
/// its own assertion (once)... Publish with verifiable identity"). No-op when the
/// application's discovery descriptor reports <c>identityPropagation.enabled: false</c> (doc
/// 06: "If the descriptor says <c>enabled: false</c>, the client attaches no header"). Caches
/// the fetched self-assertion for the lifetime of the instance (one fetch per connection, as
/// doc 06 requires), rather than re-fetching per publish.
/// </summary>
public sealed class CalloutIdentityHeader : IDisposable
{
    /// <summary>Gets the well-known fallback header name (ADR 0010), used only if the descriptor is somehow unavailable.</summary>
    /// <value>The fallback header name, <c>Callout-Identity</c>.</value>
    public static string FallbackName => "Callout-Identity";

    private readonly INatsKVContext _kv;
    private readonly DiscoveryDescriptor _descriptor;
    private readonly string _presentedOidcToken;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private string? _cachedAssertion;

    /// <summary>Initializes a new instance of the <see cref="CalloutIdentityHeader"/> class.</summary>
    /// <param name="kv">The connected client's KV context.</param>
    /// <param name="descriptor">The application's discovery descriptor.</param>
    /// <param name="presentedOidcToken">The raw OIDC token this client connected with (used for the self-assertion lookup when propagation is enabled).</param>
    public CalloutIdentityHeader(INatsKVContext kv, DiscoveryDescriptor descriptor, string presentedOidcToken)
    {
        ArgumentNullException.ThrowIfNull(kv);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentedOidcToken);
        _kv = kv;
        _descriptor = descriptor;
        _presentedOidcToken = presentedOidcToken;
    }

    /// <summary>
    /// Attaches the cached (fetching once if needed) <c>Callout-Identity</c> header to
    /// <paramref name="headers"/>, or does nothing when identity propagation is disabled for
    /// this application.
    /// </summary>
    /// <param name="headers">The headers to attach to; mutated in place. Created if null and propagation is enabled.</param>
    /// <param name="cancellationToken">Cancellation for the self-assertion fetch, if needed.</param>
    /// <returns>The headers to publish with (either <paramref name="headers"/> or a new instance), or null if propagation is disabled and none were supplied.</returns>
    public async Task<NatsHeaders?> AttachAsync(NatsHeaders? headers, CancellationToken cancellationToken = default)
    {
        if (!_descriptor.IdentityPropagation.Enabled)
        {
            return headers;
        }

        var assertion = await GetOrFetchAssertionAsync(cancellationToken).ConfigureAwait(false);
        var headerName = _descriptor.IdentityPropagation.Header ?? FallbackName;

        var result = headers ?? new NatsHeaders();
        result[headerName] = assertion;
        return result;
    }

    /// <inheritdoc />
    public void Dispose() => _fetchLock.Dispose();

    private async Task<string> GetOrFetchAssertionAsync(CancellationToken cancellationToken)
    {
        if (_cachedAssertion is not null)
        {
            return _cachedAssertion;
        }

        await _fetchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cachedAssertion ??= await CalloutSelfAssertion.FetchAsync(_kv, _descriptor, _presentedOidcToken, cancellationToken).ConfigureAwait(false);
            return _cachedAssertion;
        }
        finally
        {
            _fetchLock.Release();
        }
    }
}
