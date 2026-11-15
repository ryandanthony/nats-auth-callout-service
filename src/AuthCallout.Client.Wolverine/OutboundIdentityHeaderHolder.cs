// -----------------------------------------------------------------------
// <copyright file="OutboundIdentityHeaderHolder.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// The hand-off between <see cref="NatsTransportExtensions.UseNatsWithCallout"/> (which builds
/// <see cref="CalloutIdentityOutboundRule"/> before the DI container exists, at
/// <c>WolverineOptions</c> configuration time) and the built container's
/// <see cref="OutboundIdentityHeaderService"/> singleton. Registered itself as a DI singleton
/// and populated by <see cref="CalloutHostedConnection"/> once the container is available, so
/// the rule — constructed earlier and holding the same <see cref="OutboundIdentityHeaderHolder"/>
/// instance — can resolve it without a static field.
/// </summary>
public sealed class OutboundIdentityHeaderHolder
{
    /// <summary>Gets or sets the resolved outbound identity-header service, once the DI container is built.</summary>
    /// <value>The service, or null before <see cref="CalloutHostedConnection"/> has run.</value>
    public OutboundIdentityHeaderService? Service { get; set; }
}
