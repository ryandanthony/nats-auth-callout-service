// -----------------------------------------------------------------------
// <copyright file="CalloutTransportContext.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NATS.Client.Core;
using Wolverine.Nats.Internal;

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// The shared hand-off between the adapter's configuration-time wiring
/// (<see cref="NatsTransportExtensions.UseNatsWithCallout"/>, which runs before the DI
/// container or any connection exists) and its runtime pieces:
/// <list type="bullet">
/// <item><description><see cref="InboxPrefix"/> — computed by
/// <see cref="CalloutPrefixPrewarmService"/> (which the adapter inserts <i>ahead</i> of
/// Wolverine's own hosted runtime, so it completes first) and read synchronously by the
/// <c>ConfigureNatsOpts</c> hook when Wolverine builds its transport connection. This is what
/// lets Wolverine's single connection carry the callout's scoped inbox grant
/// (<c>_INBOX.{slug}.{principal}.&gt;</c>) instead of NATS.NET's default <c>_INBOX</c>.</description></item>
/// <item><description><see cref="Transport"/> — the Wolverine NATS transport captured at
/// configuration time, whose <see cref="NatsTransport.Connection"/> (available once Wolverine's
/// runtime has started) the identity services reuse for discovery and KV reads, eliminating the
/// separate KV-only connection earlier versions of this adapter opened (WolverineFx.Nats
/// &lt; 6.47 had no <c>ConfigureNatsOpts</c> seam).</description></item>
/// </list>
/// </summary>
public sealed class CalloutTransportContext
{
    /// <summary>
    /// Gets or sets the scoped inbox prefix (<c>_INBOX.{slug}.{principal}</c>) computed from
    /// the first fetched token, or null before <see cref="CalloutPrefixPrewarmService"/> has run.
    /// </summary>
    /// <value>The scoped inbox prefix, or null.</value>
    public string? InboxPrefix { get; set; }

    /// <summary>Gets or sets the Wolverine NATS transport captured at configuration time.</summary>
    /// <value>The transport.</value>
    public NatsTransport? Transport { get; set; }

    /// <summary>
    /// Gets or sets the OIDC token the transport's <c>AuthCallback</c> most recently presented to
    /// the callout. The callout stores this connection's signed assertion keyed by <i>this
    /// token's</i> <c>jti</c>, so the outbound identity header must be built from exactly this
    /// token, never from a second, independent <c>GetTokenAsync</c> call: a provider that mints
    /// a new token per call (or refreshes in between) would yield a different <c>jti</c> and a
    /// self-assertion lookup miss.
    /// </summary>
    /// <value>The presented token, or null before the first connect.</value>
    public string? PresentedToken { get; set; }

    /// <summary>
    /// Gets the transport's live connection.
    /// </summary>
    /// <value>The connection.</value>
    /// <exception cref="InvalidOperationException">The Wolverine runtime has not started (the transport connects during host startup).</exception>
    public INatsConnection Connection =>
        Transport?.Connection
            ?? throw new InvalidOperationException("The Wolverine NATS transport has not connected yet; identity services are available only after host startup.");
}
