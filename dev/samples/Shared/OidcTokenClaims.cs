// -----------------------------------------------------------------------
// <copyright file="OidcTokenClaims.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// Reads claims out of a compact JWT's payload/header without verifying its signature. Safe
/// for these samples' own use because every value read this way is either (a) the sample's
/// own presented token, read back only to know which self-lookup key to use — never trusted
/// as an authorization decision, that already happened inside the callout — or (b) a token
/// the sample already retrieved from a KV entry it could only read because a signature-
/// verified <see cref="AssertionVerification"/> check passed first. A real integrating
/// service should use <c>System.IdentityModel.Tokens.Jwt</c>'s <c>JwtSecurityTokenHandler</c>
/// with full validation (as <c>AuthCallout.Identity.Validation.TokenValidator</c>, P06, does)
/// wherever the claims drive a decision; these samples only ever log or self-lookup with them.
/// </summary>
public static class OidcTokenClaims
{
    private static readonly JwtSecurityTokenHandler Handler = new();

    /// <summary>Reads the token's <c>jti</c> claim, if present.</summary>
    /// <param name="compactJwt">The compact JWT.</param>
    /// <returns>The <c>jti</c> claim value, or null if absent or the token cannot be parsed.</returns>
    public static string? ReadJti(string compactJwt) => ReadClaim(compactJwt, "jti");

    /// <summary>Reads the token's <c>sub</c> claim, if present.</summary>
    /// <param name="compactJwt">The compact JWT.</param>
    /// <returns>The <c>sub</c> claim value, or null if absent or the token cannot be parsed.</returns>
    public static string? ReadSub(string compactJwt) => ReadClaim(compactJwt, "sub");

    /// <summary>Reads an arbitrary top-level claim from a compact JWT's payload.</summary>
    /// <param name="compactJwt">The compact JWT.</param>
    /// <param name="claimType">The claim name to read.</param>
    /// <returns>The claim value, or null if absent or the token cannot be parsed.</returns>
    public static string? ReadClaim(string compactJwt, string claimType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compactJwt);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimType);

        if (!Handler.CanReadToken(compactJwt))
        {
            return null;
        }

        var token = Handler.ReadJwtToken(compactJwt);
        return token.Payload.TryGetValue(claimType, out var value) ? value?.ToString() : null;
    }

    /// <summary>Reads the <c>kid</c> header field from a compact JWT (assertion or OIDC token alike).</summary>
    /// <param name="compactJwt">The compact JWT.</param>
    /// <returns>The header's <c>kid</c> value, or null if absent or the token cannot be parsed.</returns>
    public static string? ReadJwtHeaderKid(string compactJwt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compactJwt);

        if (!Handler.CanReadToken(compactJwt))
        {
            return null;
        }

        var token = Handler.ReadJwtToken(compactJwt);
        return token.Header.Kid;
    }
}
