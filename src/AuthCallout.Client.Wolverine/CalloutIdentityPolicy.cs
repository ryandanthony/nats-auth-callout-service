// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityPolicy.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Wolverine.Runtime.Handlers;

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// The filter predicate for <see cref="NatsTransportExtensions.UseNatsWithCallout"/>'s
/// <c>Policies.AddMiddleware(typeof(CalloutIdentityMiddleware), filter)</c> call (ADR 0022 task 3,
/// resolved per packet P20's open question 1 in favor of "policy so apps can't forget a
/// handler"): every handler chain gets <see cref="CalloutIdentityMiddleware"/> by default,
/// unless its message type opts out via <see cref="RequireIdentityAttribute"/>
/// (<c>[RequireIdentity(false)]</c> — packet P20's <c>RequireIdentity</c> knob for
/// listeners/subjects that take anonymous traffic, e.g. registration/discovery endpoints).
/// </summary>
/// <remarks>
/// Scoped to the message type rather than the chain's resolved endpoints deliberately:
/// <c>HandlerChain.Endpoints</c> is only populated for "sticky" per-endpoint handlers, not for
/// the common case of a message type routed to a NATS subject via
/// <c>PublishMessage&lt;T&gt;().ToNatsSubject(...)</c>/<c>ListenToNatsSubject(...)</c>, so
/// filtering on it here would silently skip most real handler chains. An app calling
/// <see cref="NatsTransportExtensions.UseNatsWithCallout"/> is, by construction, a NATS-over-the-callout
/// app, so defaulting every handler chain to verified (opt-out, not opt-in) is the correct
/// "can't forget a handler" behavior either way.
/// </remarks>
internal static class CalloutIdentityPolicy
{
    /// <summary>Whether <paramref name="chain"/> should get <see cref="CalloutIdentityMiddleware"/>.</summary>
    /// <param name="chain">The candidate handler chain.</param>
    /// <returns><see langword="true"/> unless the chain's message type opted out via <see cref="RequireIdentityAttribute"/>.</returns>
    internal static bool AppliesTo(HandlerChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        var exemption = Attribute.GetCustomAttribute(chain.MessageType, typeof(RequireIdentityAttribute)) as RequireIdentityAttribute;
        return exemption is null || exemption.Required;
    }
}
