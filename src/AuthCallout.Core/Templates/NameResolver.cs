// -----------------------------------------------------------------------
// <copyright file="NameResolver.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Core.Templates;

/// <summary>
/// Resolves the minted user JWT's <c>name</c> from a profile's <c>nameClaim</c>, falling back
/// to <c>sub</c> when absent (D19).
/// </summary>
public static class NameResolver
{
    /// <summary>
    /// Resolves the JWT name for <paramref name="nameClaim"/>: its value when present, else the
    /// token's <c>sub</c> claim (D19), else a denial.
    /// </summary>
    /// <param name="nameClaim">The profile's configured <c>nameClaim</c> path.</param>
    /// <param name="claims">The validated claims to resolve the name from.</param>
    /// <param name="name">The resolved name, when this returns null.</param>
    /// <returns>Null on success (see <paramref name="name"/>); <see cref="DenyReason.NameClaimError"/> when neither <paramref name="nameClaim"/> nor <c>sub</c> resolves to a value.</returns>
#pragma warning disable CA1021, S3874 // Out param mirrors TryXxx pattern used throughout this packet (Transform.TryParse); a discriminated-union return would add ceremony for a two-field result.
    public static DenyReason? TryResolve(string nameClaim, IClaimsAccessor claims, out string? name)
#pragma warning restore CA1021, S3874
    {
        ArgumentNullException.ThrowIfNull(nameClaim);
        ArgumentNullException.ThrowIfNull(claims);

        var direct = claims.GetSingleValue(nameClaim);
        if (direct is not null)
        {
            name = direct;
            return null;
        }

        var fallback = claims.GetSingleValue("sub");
        if (fallback is not null)
        {
            name = fallback;
            return null;
        }

        name = null;
        return DenyReason.NameClaimError;
    }
}
