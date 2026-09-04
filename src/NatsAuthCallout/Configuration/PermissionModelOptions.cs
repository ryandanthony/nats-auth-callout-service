// -----------------------------------------------------------------------
// <copyright file="PermissionModelOptions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace NatsAuthCallout.Configuration;

/// <summary>
/// Describes how connecting NATS clients are mapped to pub/sub permissions for
/// one application. The whole model is data-driven so no domain-specific logic
/// (households, subject prefixes, etc.) is baked into the service.
/// </summary>
public sealed class PermissionModelOptions
{
    /// <summary>
    /// Gets or sets the grant applied to anonymous or unverifiable clients.
    /// </summary>
    public GrantOptions Anonymous { get; set; } = new();

    /// <summary>
    /// Gets or sets the grant applied to clients presenting a valid JWT.
    /// </summary>
    public AuthenticatedGrantOptions Authenticated { get; set; } = new();
}

/// <summary>
/// A static permission grant of publish/subscribe subject patterns.
/// </summary>
public class GrantOptions
{
    /// <summary>
    /// Gets or sets the publish-allow subject patterns.
    /// </summary>
    public IList<string> Publish { get; set; } = [];

    /// <summary>
    /// Gets or sets the subscribe-allow subject patterns.
    /// </summary>
    public IList<string> Subscribe { get; set; } = [];

    /// <summary>
    /// Gets or sets the JWT lifetime in seconds. Zero or negative means the
    /// minted JWT does not expire (not recommended for anonymous grants).
    /// </summary>
    public int ExpirySeconds { get; set; }
}

/// <summary>
/// Permission grant for authenticated clients. Extends <see cref="GrantOptions"/>
/// with claim-scoped, per-value templated subjects.
/// </summary>
public sealed class AuthenticatedGrantOptions : GrantOptions
{
    /// <summary>
    /// Gets or sets the claim type whose (possibly multiple) values expand the
    /// per-scope templates. For example <c>household_id</c>.
    /// </summary>
    public string ScopeClaim { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether at least one
    /// <see cref="ScopeClaim"/> value is required. When true and the claim is
    /// absent, the client falls back to the anonymous grant.
    /// </summary>
    public bool RequireScope { get; set; } = true;

    /// <summary>
    /// Gets or sets the claim type used as the minted JWT name (e.g. <c>sub</c>).
    /// </summary>
    public string NameClaim { get; set; } = "sub";

    /// <summary>
    /// Gets or sets the publish-allow patterns expanded once per
    /// <see cref="ScopeClaim"/> value. Use <c>{scope}</c> as the placeholder.
    /// </summary>
    public IList<string> PublishPerScope { get; set; } = [];

    /// <summary>
    /// Gets or sets the subscribe-allow patterns expanded once per
    /// <see cref="ScopeClaim"/> value. Use <c>{scope}</c> as the placeholder.
    /// </summary>
    public IList<string> SubscribePerScope { get; set; } = [];

    /// <summary>
    /// Gets or sets the fallback JWT lifetime in seconds used when the presented
    /// token carries no expiry. When <see cref="GrantOptions.ExpirySeconds"/> is
    /// zero the minted JWT inherits the token's own expiry.
    /// </summary>
    public int FallbackExpirySeconds { get; set; } = 300;
}
