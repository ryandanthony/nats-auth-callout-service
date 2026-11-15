// -----------------------------------------------------------------------
// <copyright file="NatsTransportExtensions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using AuthCallout.Client.DependencyInjection;
using AuthCallout.Client.Tokens;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using Wolverine;
using Wolverine.Nats;
using Wolverine.Nats.Internal;

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// <c>opts.UseNatsWithCallout(callout =&gt; ...)</c> (ADR 0022 task 1): the Wolverine-shaped
/// equivalent of <c>AddCalloutNatsClient</c>, binding <see cref="AuthCallout.Client"/>'s token
/// provider into <c>WolverineFx.Nats</c>'s transport, plus the outbound identity-header attach
/// (task 2) and inbound verification middleware/policy (task 3) built on it.
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
    /// Wolverine owns the underlying <c>NatsConnection</c> lifecycle (unlike
    /// <see cref="CalloutNatsConnection"/>'s self-owned reconnect pipeline), so the bearer token
    /// cannot flow through a pre-built <c>NatsOpts.AuthOpts.Token</c>: it is supplied via
    /// <c>NatsTransportConfiguration.AuthCallback</c> (<c>Func&lt;Uri, CancellationToken,
    /// ValueTask&lt;NatsAuthCred&gt;&gt;</c>), which <c>WolverineFx.Nats</c> 6.x forwards straight
    /// through to <c>NatsAuthOpts.AuthCredCallback</c> and NATS.NET invokes on every (re)connect —
    /// exactly the "every (re)connect pulls a current token" requirement (ADR 0021 rules 4-5).
    /// This is the proven seam (ADR 0022's "Prior art to steal": the NKey-seed variant of the
    /// same callback already runs in production); <see cref="AuthCallout.Client"/> needed no
    /// changes to support it.
    ///
    /// <c>WolverineFx.Nats</c> 6.33's <c>NatsTransportConfiguration</c> has no
    /// <c>InboxPrefix</c>/custom-inbox knob (its <c>ToNatsOpts()</c> leaves NATS.NET's default
    /// <c>_INBOX</c> in place), but that scoping only matters for the raw JetStream KV reads this
    /// adapter needs for discovery and identity verification (the callout's grant authorizes
    /// <c>_INBOX.{slug}.{principal}.&gt;</c>, never the account-wide <c>_INBOX.&gt;</c>) — it does
    /// not apply to Wolverine's own message bus, which never uses NATS.NET's request/reply inbox
    /// (Wolverine's reply subject is <c>wolverine.response.{node}</c>, a plain subject the app's
    /// grant profile authorizes like any other). So rather than patching Wolverine's transport
    /// connection (no seam exists to), this adapter opens a second, correctly-scoped connection
    /// via <see cref="AuthCallout.Client.DependencyInjection.ServiceCollectionExtensions.AddCalloutNatsClient"/>
    /// (<see cref="CalloutNatsOpts.CreateAsync"/> already gets the scoped prefix right) purely
    /// for KV access; it is never used for Wolverine messages.
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

        var tokenProvider = calloutOptions.TokenProvider;

        // The second, correctly-scoped connection used only for discovery/identity KV reads
        // (see the <remarks> above): AddCalloutNatsClient registers CalloutNatsConnection,
        // DiscoveryDescriptorCache, and CalloutIdentityServices, none of which are shared with
        // Wolverine's own transport connection.
        options.Services.AddCalloutNatsClient(client =>
        {
            client.Url = calloutOptions.Url;
            client.ApplicationName = calloutOptions.ApplicationName;
            client.TokenProvider = tokenProvider;
            client.Name = calloutOptions.Name;
        });

        options.Services.AddSingleton(calloutOptions);
        options.Services.AddSingleton<CalloutHostedConnection>();
        options.Services.AddHostedService(sp => sp.GetRequiredService<CalloutHostedConnection>());
        options.Services.AddSingleton<OutboundIdentityHeaderService>();
        options.Services.AddSingleton<ICalloutIdentityVerifierAccessor>(sp => sp.GetRequiredService<OutboundIdentityHeaderService>());

        var outboundHeaderHolder = new OutboundIdentityHeaderHolder();
        options.Services.AddSingleton(outboundHeaderHolder);
        options.MetadataRules.Add(new CalloutIdentityOutboundRule(outboundHeaderHolder));

        // Policy-with-opt-out default (ADR 0022 open question 1, task 3): every handler chain
        // listening on a NATS endpoint gets the identity-verification middleware, unless
        // RequireIdentity(false) exempted that specific chain (CalloutIdentityPolicy.cs).
        options.Policies.AddMiddleware(typeof(CalloutIdentityMiddleware), CalloutIdentityPolicy.AppliesTo);

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

                return NatsAuthCred.FromToken(token);
            };
        });
    }
}
