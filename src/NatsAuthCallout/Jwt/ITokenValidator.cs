// -----------------------------------------------------------------------
// <copyright file="ITokenValidator.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace NatsAuthCallout.Jwt;

/// <summary>
/// Result of validating a bearer token.
/// </summary>
/// <param name="IsValid">Whether the token passed validation.</param>
/// <param name="Claims">The validated claims (type to values), empty when invalid.</param>
/// <param name="ExpiresAt">The token's own expiry, if present.</param>
/// <param name="Error">The validation error message, if any.</param>
public sealed record TokenValidationOutcome(
    bool IsValid,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Claims,
    DateTimeOffset? ExpiresAt,
    string? Error)
{
    /// <summary>
    /// Gets an invalid outcome carrying the supplied error.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <returns>An invalid outcome.</returns>
    public static TokenValidationOutcome Invalid(string? error) =>
        new(false, EmptyClaims, null, error);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyClaims =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
}

/// <summary>
/// Validates a bearer JWT and projects its claims.
/// </summary>
public interface ITokenValidator
{
    /// <summary>
    /// Validates the supplied token.
    /// </summary>
    /// <param name="token">The raw JWT.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation outcome.</returns>
    Task<TokenValidationOutcome> ValidateAsync(string token, CancellationToken cancellationToken = default);
}
