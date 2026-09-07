// -----------------------------------------------------------------------
// <copyright file="ITokenValidator.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// Strict, fail-closed OIDC/JWT token validation against an issuer's cached JWKS (ADR 0016,
/// doc 08 §Token validation). Implemented by <c>AuthCallout.Identity</c> (P06); consumed by
/// the resolution pipeline (P05) as Stage 2 (doc 03).
/// </summary>
public interface ITokenValidator
{
    /// <summary>
    /// Validates a raw bearer token against the given issuer's strict parameters (ADR 0016:
    /// non-configurable <c>ValidateIssuer</c>/<c>ValidateLifetime</c>/<c>RequireSignedTokens</c>/
    /// <c>RequireExpirationTime</c>, issuer-configured clock skew/algorithm allow-list/audience).
    /// Never throws for a malformed or otherwise invalid token — every failure mode maps to a
    /// <see cref="Decisions.DenyReason"/> in the returned <see cref="TokenValidationOutcome"/>.
    /// </summary>
    /// <param name="rawToken">The raw, undecoded bearer token.</param>
    /// <param name="issuer">The issuer configuration selected by Stage 1 (doc 03) to validate against.</param>
    /// <param name="cancellationToken">Cancellation, bounded by the per-request timeout (doc 08 §In-process parallelism).</param>
    /// <returns>The validated claims, or the specific failure reason.</returns>
    Task<TokenValidationOutcome> ValidateAsync(string rawToken, IssuerConfig issuer, CancellationToken cancellationToken);
}
