// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityOutboundRule.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Identity;

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// The outbound half of ADR 0022 task 2: an <c>IEnvelopeRule</c> registered into
/// <c>WolverineOptions.MetadataRules</c> (applied to every outgoing envelope, same seam
/// Wolverine itself uses for <c>PropagateHeadersRule</c>/<c>TenantIdRule</c>) that stamps the
/// cached <c>Callout-Identity</c> header onto the envelope, or does nothing when identity
/// propagation is disabled for this application.
/// </summary>
/// <remarks>
/// <c>IEnvelopeRule.Modify(Envelope)</c> is synchronous — Wolverine gives outgoing rules no
/// async hook — so this rule can only ever read a value <see cref="OutboundIdentityHeaderService"/>
/// already has cached; it never fetches. <see cref="CalloutHostedConnection"/> guarantees the
/// cache is warm (via <c>PrefetchAsync</c>) before Wolverine's transport starts accepting sends,
/// so by the time any envelope reaches this rule the header is always available (when identity
/// propagation is enabled).
/// </remarks>
internal sealed class CalloutIdentityOutboundRule : global::Wolverine.IEnvelopeRule
{
    private readonly OutboundIdentityHeaderHolder _holder;

    /// <summary>Initializes a new instance of the <see cref="CalloutIdentityOutboundRule"/> class.</summary>
    /// <param name="holder">
    /// The hand-off to this host's <see cref="OutboundIdentityHeaderService"/> singleton: this
    /// rule is constructed before the DI container exists (at <c>WolverineOptions</c>
    /// configuration time, in <see cref="NatsTransportExtensions.UseNatsWithCallout"/>), but
    /// shares the same <paramref name="holder"/> instance the container's
    /// <see cref="CalloutHostedConnection"/> later populates.
    /// </param>
    internal CalloutIdentityOutboundRule(OutboundIdentityHeaderHolder holder)
    {
        ArgumentNullException.ThrowIfNull(holder);
        _holder = holder;
    }

    /// <inheritdoc />
    public void Modify(global::Wolverine.Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var headerValue = _holder.Service?.TryGetCachedHeaderValue();
        if (headerValue is not null)
        {
            envelope.Headers[CalloutIdentityHeader.FallbackName] = headerValue;
        }
    }
}
