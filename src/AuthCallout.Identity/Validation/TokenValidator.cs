// -----------------------------------------------------------------------
// <copyright file="TokenValidator.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;
using AuthCallout.Core.Claims;
using AuthCallout.Identity.Jwks;
using Microsoft.IdentityModel.Tokens;

namespace AuthCallout.Identity.Validation;

/// <summary>
/// Strict, fail-closed <see cref="ITokenValidator"/> on <c>Microsoft.IdentityModel</c>
/// (doc 08 §Token validation, ADR 0016). Builds a <see cref="TokenValidationParameters"/> per
/// call from the given <see cref="IssuerConfig"/> exactly per ADR 0016's fixed and
/// per-issuer-overridable parameters, resolves signing keys only from the injected
/// <see cref="IJwksKeyProvider"/> by <c>kid</c> (never from an embedded <c>jwk</c>/<c>x5c</c>/
/// <c>jku</c> token header), and never throws for a malformed or otherwise invalid token —
/// every failure path maps to a <see cref="DenyReason"/>.
/// </summary>
public sealed class TokenValidator : ITokenValidator
{
    private static readonly JwtSecurityTokenHandler Handler = new() { MapInboundClaims = false };

    private readonly IJwksKeyProvider _keyProvider;
    private readonly IJwksCacheManager _jwksCacheManager;

    /// <summary>Initializes a new instance of the <see cref="TokenValidator"/> class.</summary>
    /// <param name="keyProvider">Provides the currently cached signing keys for an issuer, by authority.</param>
    /// <param name="jwksCacheManager">Triggers a single-flight refresh when a token's <c>kid</c> is not currently cached.</param>
    public TokenValidator(IJwksKeyProvider keyProvider, IJwksCacheManager jwksCacheManager)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        ArgumentNullException.ThrowIfNull(jwksCacheManager);
        _keyProvider = keyProvider;
        _jwksCacheManager = jwksCacheManager;
    }

    /// <inheritdoc />
    public async Task<TokenValidationOutcome> ValidateAsync(string rawToken, IssuerConfig issuer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rawToken);
        ArgumentNullException.ThrowIfNull(issuer);

        var preflightFailure = Preflight(rawToken, issuer);
        if (preflightFailure is { } reason)
        {
            return TokenValidationOutcome.Invalid(reason);
        }

        var signingKeys = _keyProvider.GetSigningKeys(issuer.Authority);
        if (signingKeys.Count == 0)
        {
            // No usable JWKS cached at all yet for this issuer: try one refresh before
            // giving up, rather than letting Microsoft.IdentityModel's "zero keys" message
            // masquerade as an ordinary signature failure.
            await _jwksCacheManager.RefreshAsync(issuer.Authority, cancellationToken);
            signingKeys = _keyProvider.GetSigningKeys(issuer.Authority);
            if (signingKeys.Count == 0)
            {
                return TokenValidationOutcome.Invalid(DenyReason.SignatureInvalid);
            }
        }

        var parameters = BuildValidationParameters(issuer, signingKeys);
        var result = await ValidateOnceAsync(rawToken, parameters, cancellationToken);

        if (!result.IsValid && IsUnknownKidFailure(result))
        {
            // doc 08 / ADR 0016: an unknown kid triggers a single-flight refresh, then one retry.
            await _jwksCacheManager.RefreshAsync(issuer.Authority, cancellationToken);
            var refreshedKeys = _keyProvider.GetSigningKeys(issuer.Authority);
            if (refreshedKeys.Count > 0)
            {
                var retryParameters = BuildValidationParameters(issuer, refreshedKeys);
                result = await ValidateOnceAsync(rawToken, retryParameters, cancellationToken);
            }
        }

        return ToOutcome(result);
    }

    /// <summary>
    /// Cheap, pre-signature-verification checks on the raw token, so a disallowed algorithm
    /// or malformed shape reports its precise <see cref="DenyReason"/> before falling through
    /// to <c>Handler.ValidateTokenAsync</c>'s generic failure mapping.
    /// </summary>
    private static DenyReason? Preflight(string rawToken, IssuerConfig issuer)
    {
        if (!Handler.CanReadToken(rawToken))
        {
            return DenyReason.SignatureInvalid;
        }

        // Peek the (unvalidated) header alg first: Microsoft.IdentityModel surfaces a
        // disallowed algorithm as the same generic SecurityTokenInvalidSignatureException as
        // an unknown kid (IDX10511 wraps the real IDX10696 cause), so checking here is what
        // lets AlgorithmRejected be reported precisely instead of collapsing into
        // SignatureInvalid (ADR 0016's alg-confusion test expects the specific reason).
        // CanReadToken only checks segment count/shape, not that each segment is valid
        // Base64Url JSON, so ReadJwtToken can still throw on a token CanReadToken accepted
        // (e.g. malformed but JWT-shaped input) — caught here so "never throws" holds for
        // every malformed input, not just the ones CanReadToken already rejects.
        string? headerAlgorithm;
        try
        {
            headerAlgorithm = Handler.ReadJwtToken(rawToken).Header.Alg;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return DenyReason.SignatureInvalid;
        }

        return IsAlgorithmAllowed(headerAlgorithm, issuer.AllowedAlgorithms) ? null : DenyReason.AlgorithmRejected;
    }

    private static bool IsAlgorithmAllowed(string? headerAlgorithm, IReadOnlyList<string> allowedAlgorithms) =>
        headerAlgorithm is not null && allowedAlgorithms.Contains(headerAlgorithm, StringComparer.Ordinal);

    private static async Task<TokenValidationResult> ValidateOnceAsync(
        string rawToken, TokenValidationParameters parameters, CancellationToken cancellationToken)
    {
        try
        {
            return await Handler.ValidateTokenAsync(rawToken, parameters).WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Handler.ValidateTokenAsync is documented to return a failed TokenValidationResult
            // rather than throw for validation failures; this catch is a last-resort guard so a
            // truly unexpected exception (e.g. a crafted token tripping an internal parser bug)
            // still denies instead of propagating out of this seam (task DoD: "never throws").
            return new TokenValidationResult { IsValid = false, Exception = ex };
        }
    }

    private static bool IsUnknownKidFailure(TokenValidationResult result) =>
        result.Exception is SecurityTokenSignatureKeyNotFoundException;

    private static TokenValidationParameters BuildValidationParameters(IssuerConfig issuer, IReadOnlyList<SecurityKey> signingKeys) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = issuer.Authority,
        ValidateLifetime = true,
        ClockSkew = issuer.ClockSkew.Value,
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ValidateAudience = issuer.ValidateAudience,
        ValidAudiences = issuer.Audience,
        ValidAlgorithms = issuer.AllowedAlgorithms,
        IssuerSigningKeys = signingKeys,

        // Keys come only from signingKeys above (sourced from the JWKS cache, by kid); the
        // token's own embedded jwk/x5c/jku header is never consulted (doc 08 / ADR 0016). No
        // custom IssuerSigningKeyResolver is installed, so nothing could fall back to an
        // attacker-supplied header key. Because signingKeys never contains a symmetric key,
        // an HS*-signed token cannot verify against them regardless of AllowedAlgorithms,
        // structurally ruling out algorithm confusion (ADR 0016 §Why HS* is excluded).
    };

    private static TokenValidationOutcome ToOutcome(TokenValidationResult result)
    {
        if (result.IsValid)
        {
            var claims = BuildClaimsAccessor(result.Claims);
            return TokenValidationOutcome.Valid(claims);
        }

        return TokenValidationOutcome.Invalid(MapFailure(result.Exception));
    }

    private static DenyReason MapFailure(Exception? exception) => exception switch
    {
        SecurityTokenExpiredException => DenyReason.Expired,
        SecurityTokenNotYetValidException => DenyReason.Expired,
        SecurityTokenNoExpirationException => DenyReason.Expired,
        SecurityTokenInvalidLifetimeException => DenyReason.Expired,
        SecurityTokenInvalidAudienceException => DenyReason.AudienceMismatch,
        SecurityTokenInvalidAlgorithmException => DenyReason.AlgorithmRejected,
        _ => DenyReason.SignatureInvalid,
    };

    private static ClaimsAccessor BuildClaimsAccessor(IDictionary<string, object> claims)
    {
        var json = JsonSerializer.Serialize(claims);
        using var document = JsonDocument.Parse(json);
        return new ClaimsAccessor(document.RootElement);
    }
}
