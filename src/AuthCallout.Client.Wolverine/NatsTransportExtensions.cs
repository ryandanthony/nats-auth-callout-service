// -----------------------------------------------------------------------
// <copyright file="NatsTransportExtensions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using Wolverine;
using Wolverine.Nats;
using Wolverine.Nats.Internal;

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// <c>opts.UseNatsWithCallout(callout =&gt; ...)</c> (ADR 0022 task 1): binds
/// <see cref="AuthCallout.Client"/>'s token provider into <c>WolverineFx.Nats</c>'s transport,
/// plus the outbound identity-header attach (task 2) and inbound verification
/// middleware/policy (task 3) built on it.
/// </summary>
public static class NatsTransportExtensions
{
    /// <summary>
    /// Configures Wolverine's NATS transport to authenticate through the callout, and wires up
    /// the identity-propagation services (<see cref="CalloutHostedConnection"/>,
    /// <see cref="OutboundIdentityHeaderService"/>, <see cref="ICalloutIdentityVerifierAccessor"/>)
    /// the adapter's outbound rule and inbound middleware/policy depend on.
    /// </summary>
    /// <remarks>
    /// Wolverine owns the underlying <c>NatsConnection</c> lifecycle, so the bearer token
    /// flows through <c>NatsTransportConfiguration.AuthCallback</c>, which NATS.NET invokes on
    /// every (re)connect — exactly the "every (re)connect pulls a current token" requirement
    /// (ADR 0021 rules 4-5).
    ///
    /// The transport's single connection also carries the callout's scoped inbox grant: the
    /// <c>ConfigureNatsOpts</c> seam (WolverineFx.Nats 6.47+) sets <c>NatsOpts.InboxPrefix</c>
    /// to <c>_INBOX.{slug}.{principal}</c>, with the principal pre-computed from the first
    /// token by <see cref="CalloutPrefixPrewarmService"/> (inserted ahead of Wolverine's own
    /// runtime in hosted-service start order, because the hook itself is synchronous). All
    /// discovery and identity KV reads then run over this same connection
    /// (<see cref="CalloutTransportContext"/>); earlier versions of this adapter had to open a
    /// second KV-only connection because the seam didn't exist.
    /// </remarks>
    /// <param name="options">The Wolverine options being configured.</param>
    /// <param name="configure">Configures <see cref="CalloutWolverineOptions"/> (URL, application name, token provider).</param>
    /// <returns>The Wolverine NATS transport expression, for further transport configuration (JetStream, TLS, etc.).</returns>
    public static NatsTransportExpression UseNatsWithCallout(this global::Wolverine.WolverineOptions options, Action<CalloutWolverineOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);

        var calloutOptions = new CalloutWolverineOptions();
        configure(calloutOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(calloutOptions.ApplicationName);
        if (calloutOptions.TokenProvider is null)
        {
            throw new ArgumentException("TokenProvider must be set (directly, or via UseClientCredentials).", nameof(configure));
        }

        var transportContext = new CalloutTransportContext();
        RegisterIdentityServices(options, calloutOptions, transportContext);

        var expression = ConfigureTransport(options, calloutOptions, transportContext);
        transportContext.Transport = options.Transports.GetOrCreate<NatsTransport>();
        return expression;
    }

    private static void RegisterIdentityServices(global::Wolverine.WolverineOptions options, CalloutWolverineOptions calloutOptions, CalloutTransportContext transportContext)
    {
        options.Services.AddSingleton(calloutOptions);
        options.Services.AddSingleton(transportContext);

        // Start order matters twice here (see CalloutPrefixPrewarmService's doc comment):
        // the prefix prewarm must run BEFORE Wolverine's runtime (which Wolverine registered
        // before this configure callback ran) connects the transport, so it is inserted at the
        // front of the service collection; the header prefetch must run AFTER (it reads KV over
        // the transport's connection), so it is appended normally below.
        options.Services.Insert(
            0,
            ServiceDescriptor.Singleton<IHostedService>(sp => ActivatorUtilities.CreateInstance<CalloutPrefixPrewarmService>(sp)));

        options.Services.AddSingleton<OutboundIdentityHeaderService>();
        options.Services.AddSingleton<ICalloutIdentityVerifierAccessor>(sp => sp.GetRequiredService<OutboundIdentityHeaderService>());

        var outboundHeaderHolder = new OutboundIdentityHeaderHolder();
        options.Services.AddSingleton(outboundHeaderHolder);
        options.MetadataRules.Add(new CalloutIdentityOutboundRule(outboundHeaderHolder));
        options.Services.AddSingleton<CalloutHostedConnection>();
        options.Services.AddHostedService(sp => sp.GetRequiredService<CalloutHostedConnection>());

        // Policy-with-opt-out default (ADR 0022 open question 1, task 3): every handler chain
        // listening on a NATS endpoint gets the identity-verification middleware, unless
        // RequireIdentity(false) exempted that specific chain (CalloutIdentityPolicy.cs).
        options.Policies.AddMiddleware(typeof(CalloutIdentityMiddleware), CalloutIdentityPolicy.AppliesTo);
    }

    private static NatsTransportExpression ConfigureTransport(global::Wolverine.WolverineOptions options, CalloutWolverineOptions calloutOptions, CalloutTransportContext transportContext)
    {
        var tokenProvider = calloutOptions.TokenProvider!;
        return options.UseNats(transport =>
        {
            transport.ConnectionString = calloutOptions.Url;
            transport.ClientName = calloutOptions.Name;

            // Runs on every (re)connect, so the token is never stale (ADR 0021 rules 4-5):
            // fetch the current token fresh each time rather than capturing one.
            transport.AuthCallback = async (_, cancellationToken) =>
            {
                var token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
                if (token is null)
                {
                    throw new CalloutAuthenticationRequiredException();
                }

                // Record exactly what was presented: the callout keys this connection's
                // self-assertion by this token's jti (see CalloutTransportContext.PresentedToken).
                transportContext.PresentedToken = token;
                return NatsAuthCred.FromToken(token);
            };

            // Scope the transport connection's inbox to the callout's grant. The prewarm
            // service has already populated the prefix by the time Wolverine's runtime builds
            // the connection and invokes this synchronous hook.
            transport.ConfigureNatsOpts = opts =>
                transportContext.InboxPrefix is { } prefix ? opts with { InboxPrefix = prefix } : opts;
        });
    }
}
