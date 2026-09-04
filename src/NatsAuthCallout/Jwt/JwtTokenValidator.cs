// -----------------------------------------------------------------------
// <copyright file="JwtTokenValidator.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NatsAuthCallout.Configuration;

namespace NatsAuthCallout.Jwt;

/// <summary>
/// Validates bearer JWTs against an OIDC provider's JWKS using the configured
/// issuer/audience rules.
/// </summary>
public sealed class JwtTokenValidator : ITokenValidator
{
    private readonly IJwksProvider _jwksProvider;
    private readonly TokenValidationParameters _baseParameters;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtTokenValidator"/> class.
    /// </summary>
    /// <param name="options">The JWT validation options.</param>
    /// <param name="jwksProvider">The signing-key provider.</param>
    public JwtTokenValidator(JwtValidationOptions options, IJwksProvider jwksProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        _jwksProvider = jwksProvider ?? throw new ArgumentNullException(nameof(jwksProvider));

        _baseParameters = new TokenValidationParameters
        {
            ValidateIssuer = options.ValidateIssuer,
            ValidIssuer = options.Authority,
            ValidateAudience = options.ValidateAudience,
            ValidAudience = options.Audience,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
        };
    }

    /// <inheritdoc/>
    public async Task<TokenValidationOutcome> ValidateAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        // Clone to avoid mutating shared parameters across concurrent callouts.
        var parameters = _baseParameters.Clone();
        parameters.IssuerSigningKeys = await _jwksProvider.GetSigningKeysAsync(cancellationToken);

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token, parameters);

        if (!result.IsValid)
        {
            return TokenValidationOutcome.Invalid(result.Exception?.Message);
        }

        var claims = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var group in result.ClaimsIdentity.Claims.GroupBy(c => c.Type, StringComparer.Ordinal))
        {
            claims[group.Key] = group.Select(c => c.Value).ToList();
        }

        DateTimeOffset? expiresAt = result.SecurityToken is JsonWebToken jwt
            ? new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero)
            : null;

        return new TokenValidationOutcome(true, claims, expiresAt, null);
    }
}
