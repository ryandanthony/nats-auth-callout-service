// -----------------------------------------------------------------------
// <copyright file="NatsImplicitTls.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NATS.Client.Core;

namespace AuthCallout.Host.Wiring;

/// <summary>
/// Resolves the <see cref="NatsTlsOpts"/> a <see cref="NatsConnection"/> should use for a given
/// NATS URL. Split out of <see cref="ComponentGraph"/> purely to keep that class's dependency
/// count under the repo's class-coupling analyzer limit — not because this logic is otherwise
/// coupled to anything else in the component graph.
/// </summary>
public static class NatsImplicitTls
{
    /// <summary>
    /// NATS.NET's URL scheme alone does not select implicit (TLS-first) TLS: the client still
    /// waits for the server's plaintext INFO protocol message before negotiating TLS unless
    /// <see cref="NatsTlsOpts.Mode"/> is explicitly set to <see cref="TlsMode.Implicit"/>. A
    /// server configured with <c>tls { handshake_first: true }</c> (required for SNI-based
    /// stream routing in front of the server) sends the TLS handshake before INFO, so a client
    /// that hasn't opted into <see cref="TlsMode.Implicit"/> hangs or fails to connect. This
    /// opts in automatically whenever any comma-separated URL in <paramref name="natsUrl"/>
    /// uses the <c>tls://</c> scheme, so operators only need to pick the URL scheme and the
    /// matching server-side <c>handshake_first</c> setting — the system CA trust store (the
    /// <see cref="NatsTlsOpts"/> default) is sufficient for a public CA-issued certificate.
    /// </summary>
    /// <param name="natsUrl">The configured NATS URL (possibly comma-separated seed URLs).</param>
    /// <returns><see cref="TlsMode.Implicit"/> opts when any URL uses <c>tls://</c>; otherwise the default opts.</returns>
    public static NatsTlsOpts ResolveTlsOpts(string natsUrl)
    {
        ArgumentNullException.ThrowIfNull(natsUrl);

        return Array.Exists(natsUrl.Split(','), static u => u.Trim().StartsWith("tls://", StringComparison.OrdinalIgnoreCase))
            ? new NatsTlsOpts { Mode = TlsMode.Implicit }
            : NatsTlsOpts.Default;
    }
}
