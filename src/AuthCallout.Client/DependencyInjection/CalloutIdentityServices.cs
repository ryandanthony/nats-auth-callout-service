// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityServices.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using AuthCallout.Client.Identity;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Net;

namespace AuthCallout.Client.DependencyInjection;

/// <summary>
/// The async entry point to the identity-verification side of
/// <see cref="ServiceCollectionExtensions.AddCalloutNatsClient"/>: resolving
/// <see cref="ICalloutIdentityVerifier"/> needs the application's discovery descriptor, which
/// requires an async KV fetch against a live connection. A synchronous DI factory cannot await
/// that without blocking the thread (sync-over-async), so this type exposes the equivalent
/// async accessor instead, resolved once (per application name) and cached for the provider's
/// lifetime.
/// </summary>
public sealed class CalloutIdentityServices : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private ICalloutIdentityVerifier? _verifier;

    /// <summary>Initializes a new instance of the <see cref="CalloutIdentityServices"/> class.</summary>
    /// <param name="serviceProvider">The container to resolve <see cref="CalloutClientOptions"/>, <see cref="CalloutNatsConnection"/>, and <see cref="DiscoveryDescriptorCache"/> from.</param>
    public CalloutIdentityServices(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets the <see cref="ICalloutIdentityVerifier"/> for this client's application,
    /// building it on first call (fetching the discovery descriptor via the connected KV
    /// context) and caching it thereafter.
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the discovery fetch, on first call.</param>
    /// <returns>The identity verifier.</returns>
    /// <exception cref="InvalidOperationException">The registered <see cref="CalloutNatsConnection"/> has not connected yet.</exception>
    public async Task<ICalloutIdentityVerifier> GetIdentityVerifierAsync(CancellationToken cancellationToken = default)
    {
        if (_verifier is not null)
        {
            return _verifier;
        }

        await _buildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_verifier is null)
            {
                var options = _serviceProvider.GetRequiredService<CalloutClientOptions>();
                var managed = _serviceProvider.GetRequiredService<CalloutNatsConnection>();
                var connection = managed.Connection
                    ?? throw new InvalidOperationException("CalloutNatsConnection has not connected yet; call ConnectAsync/ConnectAnonymousAsync first.");

                var kv = ((INatsConnection)connection).CreateKeyValueStoreContext();
                var cache = _serviceProvider.GetRequiredService<DiscoveryDescriptorCache>();
                var descriptor = await cache.GetOrFetchAsync(kv, options.ApplicationName, cancellationToken).ConfigureAwait(false);
                var resolver = new CalloutIdentityResolver(kv, descriptor, _serviceProvider.GetService<TimeProvider>());
                _verifier = new CalloutIdentityVerifier(resolver);
            }

            return _verifier;
        }
        finally
        {
            _buildLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _buildLock.Dispose();
}
