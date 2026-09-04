// -----------------------------------------------------------------------
// <copyright file="AuthCalloutOptions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace NatsAuthCallout.Configuration;

/// <summary>
/// Root configuration for the NATS auth callout service. Bound from
/// configuration (JSON + environment variables + CLI args).
/// </summary>
/// <remarks>
/// A single callout service instance can serve multiple applications, each with
/// its own OIDC issuer, permission model, and target NATS account. Connections
/// are routed to an application by <see cref="Routing.ApplicationSelector"/>.
/// </remarks>
public sealed class AuthCalloutOptions
{
    /// <summary>
    /// Gets or sets the NATS server URL the callout service connects to.
    /// </summary>
    public string Url { get; set; } = "nats://localhost:4222";

    /// <summary>
    /// Gets or sets the NKey seed used to sign minted user JWTs and the
    /// authorization response. This is the account signing key configured as
    /// the <c>issuer</c> in the NATS <c>auth_callout</c> block. Shared across
    /// applications (a single AUTH account signs every user JWT).
    /// </summary>
    public string IssuerSeed { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the NKey seed for the callout service's own NATS user
    /// (a member of the AUTH account listed in <c>auth_users</c>). Optional:
    /// when empty the service connects anonymously.
    /// </summary>
    public string ServiceUserSeed { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the applications served by this callout instance.
    /// </summary>
    public IList<ApplicationOptions> Applications { get; set; } = [];

    /// <summary>
    /// Gets or sets the name of the application used when a connection matches
    /// no application's routing rules. When empty, unmatched connections that
    /// present no valid token receive that application's anonymous grant; if no
    /// default is set, unmatched connections are denied (minimal permissions).
    /// </summary>
    public string DefaultApplication { get; set; } = string.Empty;
}
