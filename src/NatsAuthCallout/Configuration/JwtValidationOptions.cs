// -----------------------------------------------------------------------
// <copyright file="JwtValidationOptions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace NatsAuthCallout.Configuration;

/// <summary>
/// Options controlling validation of the incoming bearer JWT (e.g. a Keycloak
/// access token) presented by a connecting client.
/// </summary>
public sealed class JwtValidationOptions
{
    /// <summary>
    /// Gets or sets the OIDC authority (issuer). Used both as the expected
    /// issuer and to derive the JWKS URL when <see cref="JwksUri"/> is unset.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the explicit JWKS URI. When empty it is derived from
    /// <see cref="Authority"/> as <c>{Authority}/protocol/openid-connect/certs</c>.
    /// </summary>
    public string JwksUri { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the issuer is validated.
    /// </summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the audience is validated.
    /// </summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// Gets or sets the expected audience. Ignored when
    /// <see cref="ValidateAudience"/> is <c>false</c>.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JWKS cache lifetime in minutes.
    /// </summary>
    public int JwksCacheMinutes { get; set; } = 60;

    /// <summary>
    /// Gets the effective JWKS URI, deriving it from the authority when needed.
    /// </summary>
    /// <returns>The absolute JWKS URI.</returns>
    public Uri ResolveJwksUri()
    {
        if (!string.IsNullOrWhiteSpace(JwksUri))
        {
            return new Uri(JwksUri);
        }

        if (string.IsNullOrWhiteSpace(Authority))
        {
            throw new InvalidOperationException(
                "Jwt.Authority or Jwt.JwksUri must be configured for JWT validation.");
        }

        return new Uri($"{Authority.TrimEnd('/')}/protocol/openid-connect/certs");
    }
}
