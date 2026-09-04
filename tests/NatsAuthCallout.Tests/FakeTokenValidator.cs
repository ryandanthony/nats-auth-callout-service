// -----------------------------------------------------------------------
// <copyright file="FakeTokenValidator.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NatsAuthCallout.Jwt;

namespace NatsAuthCallout.Tests;

/// <summary>
/// In-memory token validator for unit tests. Returns pre-seeded outcomes keyed
/// by the raw token string.
/// </summary>
internal sealed class FakeTokenValidator : ITokenValidator
{
    private readonly Dictionary<string, TokenValidationOutcome> _outcomes = new(StringComparer.Ordinal);

    public FakeTokenValidator AddValid(
        string token,
        IReadOnlyDictionary<string, IReadOnlyList<string>> claims,
        DateTimeOffset? expiresAt = null)
    {
        _outcomes[token] = new TokenValidationOutcome(true, claims, expiresAt, null);
        return this;
    }

    public FakeTokenValidator AddInvalid(string token, string error = "invalid")
    {
        _outcomes[token] = TokenValidationOutcome.Invalid(error);
        return this;
    }

    public Task<TokenValidationOutcome> ValidateAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            _outcomes.TryGetValue(token, out var outcome)
                ? outcome
                : TokenValidationOutcome.Invalid("unknown token"));
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Claims(
        params (string Type, string[] Values)[] claims)
    {
        return claims.ToDictionary(
            c => c.Type,
            c => (IReadOnlyList<string>)c.Values,
            StringComparer.Ordinal);
    }
}
