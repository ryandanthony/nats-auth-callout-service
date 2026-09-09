// -----------------------------------------------------------------------
// <copyright file="IJwksKeyProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.IdentityModel.Tokens;

namespace AuthCallout.Identity.Jwks;

/// <summary>
/// This packet's own seam (not in <c>AuthCallout.Contracts</c>, which is frozen) between
/// <see cref="AuthCallout.Identity.Validation.TokenValidator"/> and
/// <see cref="JwksCacheManager"/>: the currently cached signing keys for one issuer, by
/// authority. <see cref="AuthCallout.Contracts.Seams.IJwksCacheManager"/> (Contracts) only
/// exposes prewarm/refresh/health, not key lookup, so <see cref="JwksCacheManager"/>
/// additionally implements this interface and <c>TokenValidator</c> depends on it directly
/// (both registered as the same singleton instance by whoever wires DI, P13).
/// </summary>
public interface IJwksKeyProvider
{
    /// <summary>
    /// Gets every currently cached signing key for <paramref name="issuerAuthority"/>, empty
    /// when nothing has been fetched yet (a validator encountering this should trigger
    /// <see cref="AuthCallout.Contracts.Seams.IJwksCacheManager.RefreshAsync"/> before giving up).
    /// </summary>
    /// <param name="issuerAuthority">The issuer authority to look up cached keys for.</param>
    /// <returns>Every currently cached signing key for the issuer; empty when none are cached.</returns>
    IReadOnlyList<SecurityKey> GetSigningKeys(string issuerAuthority);
}
