// -----------------------------------------------------------------------
// <copyright file="ApplicationOptions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace NatsAuthCallout.Configuration;

/// <summary>
/// Configuration for a single application served by the callout instance.
/// Each application has its own OIDC issuer, permission model, and target NATS
/// account, so two applications sharing one callout get independent permissions.
/// </summary>
public sealed class ApplicationOptions
{
    /// <summary>
    /// Gets or sets the application name (used for routing, logging, and metrics).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the NATS account minted users are placed into (the
    /// <c>Audience</c> on the user JWT). Each application typically maps to its
    /// own NATS account. Defaults to <c>APP</c>.
    /// </summary>
    public string TargetAccount { get; set; } = "APP";

    /// <summary>
    /// Gets or sets the rules that route a connection to this application.
    /// </summary>
    public RoutingOptions Routing { get; set; } = new();

    /// <summary>
    /// Gets or sets the JWT validation options for this application's tokens.
    /// </summary>
    public JwtValidationOptions Jwt { get; set; } = new();

    /// <summary>
    /// Gets or sets the permission model for this application.
    /// </summary>
    public PermissionModelOptions Permissions { get; set; } = new();
}

/// <summary>
/// Rules that route an inbound connection to an application.
/// </summary>
public sealed class RoutingOptions
{
    /// <summary>
    /// Gets or sets service-account NKey public keys that both route to this
    /// application and bypass JWT validation with a full-access grant.
    /// </summary>
    public IList<string> ServiceAccountNKeys { get; set; } = [];

    /// <summary>
    /// Gets or sets the NATS connect usernames that route to this application.
    /// A client selects its application explicitly by connecting with one of
    /// these as the NATS username.
    /// </summary>
    public IList<string> ConnectUsernames { get; set; } = [];

    /// <summary>
    /// Gets or sets bearer-token issuers (the <c>iss</c> claim) that route to
    /// this application. Lets clients be routed purely by their OIDC provider.
    /// </summary>
    public IList<string> TokenIssuers { get; set; } = [];
}
