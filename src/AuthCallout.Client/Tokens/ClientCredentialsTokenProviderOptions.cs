// -----------------------------------------------------------------------
// <copyright file="ClientCredentialsTokenProviderOptions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Client.Tokens;

/// <summary>
/// Options for <see cref="ClientCredentialsTokenProvider"/> (ADR 0002: a service connects as
/// just another OIDC principal via the client-credentials grant).
/// </summary>
public sealed class ClientCredentialsTokenProviderOptions
{
    /// <summary>
    /// Gets or sets the OIDC token endpoint to POST the client-credentials grant to. Mutually
    /// exclusive with <see cref="Authority"/>: set exactly one.
    /// </summary>
    /// <value>The full token endpoint URL, or null when using <see cref="Authority"/> discovery.</value>
    public Uri? TokenEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the issuer authority to discover the token endpoint from via
    /// <c>{authority}/.well-known/openid-configuration</c>'s <c>token_endpoint</c>. Mutually
    /// exclusive with <see cref="TokenEndpoint"/>: set exactly one.
    /// </summary>
    /// <value>The issuer authority base URL, or null when using an explicit <see cref="TokenEndpoint"/>.</value>
    public Uri? Authority { get; set; }

    /// <summary>Gets or sets the OIDC client id.</summary>
    /// <value>The client id.</value>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the client secret reference (inline/file/k8s-secret; ADR 0005).</summary>
    /// <value>The client secret reference.</value>
    public SecretRef? ClientSecret { get; set; }

    /// <summary>Gets or sets the requested scopes, space-joined into the <c>scope</c> form field when non-empty.</summary>
    /// <value>The requested scopes.</value>
    public IReadOnlyList<string>? Scopes { get; set; }

    /// <summary>Gets or sets the requested audience, if the authorization server supports the <c>audience</c> parameter.</summary>
    /// <value>The requested audience, or null.</value>
    public string? Audience { get; set; }

    /// <summary>
    /// Gets or sets the margin before <c>exp</c> at which the provider proactively re-mints
    /// (ADR 0021 rule 5). Defaults to 30 seconds.
    /// </summary>
    /// <value>The refresh margin.</value>
    public TimeSpan RefreshMargin { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long to back off after a failed re-mint before trying again, keeping
    /// the stale token meanwhile (ADR 0021 rule 5). Defaults to 5 seconds.
    /// </summary>
    /// <value>The failed-re-mint backoff.</value>
    public TimeSpan FailedRefreshBackoff { get; set; } = TimeSpan.FromSeconds(5);
}
