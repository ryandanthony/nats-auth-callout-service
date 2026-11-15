// -----------------------------------------------------------------------
// <copyright file="CalloutHostedConnection.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using Microsoft.Extensions.Hosting;

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// Opens, at host startup, the second callout-authenticated connection
/// <see cref="NatsTransportExtensions.UseNatsWithCallout"/> registers via
/// <c>AddCalloutNatsClient</c> (see that method's remarks): used only for discovery and
/// identity-verification KV reads, never for Wolverine's own NATS traffic. Also prefetches the
/// outbound identity header (<see cref="OutboundIdentityHeaderService"/>) so
/// <see cref="CalloutIdentityOutboundRule"/>'s synchronous <c>Modify</c> never has to.
/// </summary>
public sealed class CalloutHostedConnection : IHostedService
{
    private readonly CalloutNatsConnection _connection;
    private readonly OutboundIdentityHeaderService _outboundHeader;
    private readonly OutboundIdentityHeaderHolder _holder;

    /// <summary>Initializes a new instance of the <see cref="CalloutHostedConnection"/> class.</summary>
    /// <param name="connection">The managed connection registered by <c>AddCalloutNatsClient</c>.</param>
    /// <param name="outboundHeader">The outbound identity-header service to prefetch.</param>
    /// <param name="holder">The hand-off this instance populates for <see cref="CalloutIdentityOutboundRule"/>.</param>
    public CalloutHostedConnection(CalloutNatsConnection connection, OutboundIdentityHeaderService outboundHeader, OutboundIdentityHeaderHolder holder)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(outboundHeader);
        ArgumentNullException.ThrowIfNull(holder);
        _connection = connection;
        _outboundHeader = outboundHeader;
        _holder = holder;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _outboundHeader.PrefetchAsync(cancellationToken).ConfigureAwait(false);
        _holder.Service = _outboundHeader;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken) => await _connection.DisposeAsync().ConfigureAwait(false);
}
