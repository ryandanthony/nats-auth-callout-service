// -----------------------------------------------------------------------
// <copyright file="CalloutHostedConnection.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Hosting;

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// Warms the outbound identity header after Wolverine's transport has connected (this service
/// is registered after Wolverine's runtime, so hosted-service start order guarantees the
/// transport connection exists): prefetches the discovery descriptor and this connection's
/// signed assertion over the transport's own (scoped-inbox) connection, so
/// <see cref="CalloutIdentityOutboundRule"/>'s synchronous <c>Modify</c> only ever reads a
/// cache. See <see cref="CalloutTransportContext"/> for the single-connection design.
/// </summary>
public sealed class CalloutHostedConnection : IHostedService
{
    private readonly OutboundIdentityHeaderService _outboundHeader;
    private readonly OutboundIdentityHeaderHolder _holder;

    /// <summary>Initializes a new instance of the <see cref="CalloutHostedConnection"/> class.</summary>
    /// <param name="outboundHeader">The outbound identity-header service to prefetch.</param>
    /// <param name="holder">The hand-off this instance populates for <see cref="CalloutIdentityOutboundRule"/>.</param>
    public CalloutHostedConnection(OutboundIdentityHeaderService outboundHeader, OutboundIdentityHeaderHolder holder)
    {
        ArgumentNullException.ThrowIfNull(outboundHeader);
        ArgumentNullException.ThrowIfNull(holder);
        _outboundHeader = outboundHeader;
        _holder = holder;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _outboundHeader.PrefetchAsync(cancellationToken).ConfigureAwait(false);
        _holder.Service = _outboundHeader;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
