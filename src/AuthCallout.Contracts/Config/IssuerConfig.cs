// -----------------------------------------------------------------------
// <copyright file="IssuerConfig.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Internal;
using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// The OIDC authority an application's tokens are validated against, plus the strict,
/// fail-closed token-validation parameters pinned by ADR 0016. Validation parameters live
/// only here, never on a profile, so no profile can weaken them (ADR 0016 §Placement).
/// </summary>
/// <param name="Authority">The OIDC issuer authority. Selects candidate applications and (absent <see cref="JwksUri"/>) the JWKS via discovery.</param>
/// <param name="JwksUri">Explicit JWKS URI override; when null, resolved from <c>{Authority}/.well-known/openid-configuration</c>'s <c>jwks_uri</c>.</param>
/// <param name="JwksCache">Cache lifetime for this issuer's JWKS. Should be greater than or equal to the effective refresh interval so keys never expire between refreshes.</param>
/// <param name="RefreshInterval">Optional per-issuer override of the bootstrap <c>jwks.refreshInterval</c> proactive-refresh cadence.</param>
/// <param name="ValidateAudience">Whether to validate the <c>aud</c> claim (ADR 0016). Default true. When true, <see cref="Audience"/> is required.</param>
/// <param name="Audience">One or more accepted audiences. Required when <see cref="ValidateAudience"/> is true; otherwise ignored.</param>
/// <param name="ClockSkew">Allowed clock skew for lifetime validation. Default <c>60s</c>, capped at <c>300s</c> at config load (ADR 0016).</param>
/// <param name="AllowedAlgorithms">Explicit asymmetric-only signing algorithm allow-list. <c>HS*</c>/<c>none</c> are a config-load error (ADR 0016).</param>
public sealed record IssuerConfig(
    string Authority,
    string? JwksUri,
    DurationString JwksCache,
    DurationString? RefreshInterval,
    bool ValidateAudience,
    IReadOnlyList<string>? Audience,
    DurationString ClockSkew,
    IReadOnlyList<string> AllowedAlgorithms)
{
    /// <summary>Gets the default clock skew per ADR 0016 (<c>60s</c>).</summary>
    /// <value>The default clock skew, <c>60s</c>.</value>
    public static DurationString DefaultClockSkew { get; } = DurationString.Parse("60s");

    /// <summary>Gets the hard cap on <see cref="ClockSkew"/> per ADR 0016 (<c>300s</c>).</summary>
    /// <value>The clock skew cap, <c>300s</c>.</value>
    public static DurationString ClockSkewCap { get; } = DurationString.Parse("300s");

    /// <summary>Gets the asymmetric-only default allow-list per ADR 0016.</summary>
    /// <value>The default asymmetric algorithm allow-list.</value>
    public static IReadOnlyList<string> DefaultAllowedAlgorithms { get; } =
        ["RS256", "RS384", "RS512", "ES256", "ES384", "ES512", "PS256", "PS384", "PS512"];

    /// <inheritdoc />
    public bool Equals(IssuerConfig? other)
    {
        if (other is null)
        {
            return false;
        }

        var identityEqual = string.Equals(Authority, other.Authority, StringComparison.Ordinal)
            && string.Equals(JwksUri, other.JwksUri, StringComparison.Ordinal)
            && JwksCache.Equals(other.JwksCache);
        var validationEqual = RefreshInterval.Equals(other.RefreshInterval)
            && ValidateAudience == other.ValidateAudience
            && ClockSkew.Equals(other.ClockSkew);
        return identityEqual
            && validationEqual
            && StructuralEquality.SequenceEqual(Audience, other.Audience)
            && StructuralEquality.SequenceEqual(AllowedAlgorithms, other.AllowedAlgorithms);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Authority);
        hash.Add(JwksUri);
        hash.Add(JwksCache);
        hash.Add(RefreshInterval);
        hash.Add(ValidateAudience);
        hash.Add(ClockSkew);
        hash.Add(StructuralEquality.SequenceHashCode(Audience));
        hash.Add(StructuralEquality.SequenceHashCode(AllowedAlgorithms));
        return hash.ToHashCode();
    }
}
