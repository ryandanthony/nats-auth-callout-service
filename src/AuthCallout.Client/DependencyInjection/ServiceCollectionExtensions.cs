// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using AuthCallout.Client.Identity;
using AuthCallout.Client.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NATS.Client.Core;

namespace AuthCallout.Client.DependencyInjection;

/// <summary>
/// <c>services.AddCalloutNatsClient(...)</c> (packet P19 task 5): registers
/// <see cref="ICalloutTokenProvider"/>, a singleton <see cref="CalloutNatsConnection"/>,
/// the resolved <see cref="INatsConnection"/>, <see cref="ICalloutIdentityVerifier"/>, and a
/// per-application discovery-descriptor cache.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the callout client library's services, configured via <paramref name="configure"/>.
    /// <see cref="CalloutNatsConnection"/> must be connected (<c>ConnectAsync</c>/<c>ConnectAnonymousAsync</c>)
    /// before resolving <see cref="INatsConnection"/> or <see cref="ICalloutIdentityVerifier"/>
    /// (both of which need a live connection); typically done once during host startup.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Configures <see cref="CalloutClientOptions"/> (URL, application name, token provider or anonymous opt-in, etc.).</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddCalloutNatsClient(this IServiceCollection services, Action<CalloutClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new CalloutClientOptions();
        configure(options);
        services.AddSingleton(options);

        if (options.TokenProvider is not null)
        {
            services.TryAddSingleton(options.TokenProvider);
        }

        services.TryAddSingleton(sp => new CalloutNatsConnection(
            sp.GetRequiredService<CalloutClientOptions>(),
            sp.GetService<TimeProvider>(),
            sp.GetService<ConnectBackoff>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<CalloutNatsConnection>>()));

        services.TryAddSingleton<INatsConnection>(sp =>
        {
            var managed = sp.GetRequiredService<CalloutNatsConnection>();
            return managed.Connection
                ?? throw new InvalidOperationException(
                    "CalloutNatsConnection has not connected yet. Call ConnectAsync/ConnectAnonymousAsync before resolving INatsConnection, " +
                    "or resolve CalloutNatsConnection directly and connect it during startup.");
        });

        services.TryAddSingleton<DiscoveryDescriptorCache>();

        // CalloutIdentityResolver needs the application's discovery descriptor, which requires
        // an async KV fetch. A synchronous DI factory cannot await that without blocking
        // (sync-over-async); instead this registers an async accessor
        // (CalloutIdentityServices.GetIdentityVerifierAsync) that resolves everything once the
        // connection is up, and caches the result for the lifetime of the provider.
        services.TryAddSingleton<CalloutIdentityServices>();

        return services;
    }
}
