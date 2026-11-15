// -----------------------------------------------------------------------
// <copyright file="CalloutClientOptions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Tokens;

namespace AuthCallout.Client.Connection;

/// <summary>
/// Options for <see cref="CalloutNatsOpts.CreateAsync"/> and <see cref="CalloutNatsConnection"/>:
/// everything needed to build a correct <c>NatsOpts</c> for a connection through the callout
/// (ADR 0020), authenticated or anonymous.
/// </summary>
public sealed class CalloutClientOptions
{
    /// <summary>Gets or sets the NATS server URL(s) to connect to.</summary>
    /// <value>The server URL, e.g. <c>nats://localhost:4222</c>.</value>
    public string Url { get; set; } = "nats://localhost:4222";

    /// <summary>
    /// Gets or sets the application's authored name (slugified via <c>AppSlug</c>, ADR 0013),
    /// used to derive the scoped inbox prefix for the authenticated path.
    /// </summary>
    /// <value>The application name.</value>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the token provider for the authenticated path. Required unless
    /// <see cref="Anonymous"/> is true.
    /// </summary>
    /// <value>The token provider, or null when connecting anonymously.</value>
    public ICalloutTokenProvider? TokenProvider { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this connection is an explicit anonymous
    /// opt-in (ADR 0021 rule 3: "anonymous is an explicit caller opt-in", never an automatic
    /// fallback from a missing token).
    /// </summary>
    /// <value><see langword="true"/> to connect anonymously.</value>
    public bool Anonymous { get; set; }

    /// <summary>
    /// Gets or sets the inbox prefix to use for an anonymous connection (app policy, not NACS
    /// policy). Defaults to <c>{slug}._INBOX</c> when unset and <see cref="Anonymous"/> is true.
    /// </summary>
    /// <value>The anonymous inbox prefix, or null to use the default.</value>
    public string? AnonymousInboxPrefix { get; set; }

    /// <summary>Gets or sets the connection's display name (<c>NatsOpts.Name</c>).</summary>
    /// <value>The connection name, or null to use the NATS.Net default.</value>
    public string? Name { get; set; }
}
