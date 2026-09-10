// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityResolver.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NATS.Client.KeyValueStore;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// The subscriber-side half of doc 06 "How the subscriber resolves the caller's identity":
/// verify the <c>Callout-Identity</c> header's signature against the keys bucket (cached per
/// <c>kid</c>), then, only once verified, optionally resolve the caller's OIDC token from the
/// token-store bucket (requires the subscribing profile's <c>autoAddSessionAccess</c>).
/// </summary>
public sealed class CalloutIdentityResolver(INatsKVContext kv, DiscoveryDescriptor descriptor, TimeProvider? timeProvider = null)
{
    private readonly Dictionary<string, string> _publicKeyCache = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Verifies a <c>Callout-Identity</c> header value's signature and returns the verified
    /// claims, or null if verification fails for any reason (missing <c>kid</c>, unknown
    /// key, bad signature, expired). Never throws on a malformed/forged header — a message
    /// with no valid header conveys no identity (ADR 0010) and callers should treat a null
    /// result as "unverified", not as an error.
    /// </summary>
    /// <param name="compactAssertionJwt">The raw header value.</param>
    /// <returns>The verified claims, or null.</returns>
    public async Task<AuthCallout.Contracts.Seams.AssertionClaims?> VerifyAsync(string compactAssertionJwt)
    {
        var kid = OidcTokenClaims.ReadJwtHeaderKid(compactAssertionJwt);
        if (kid is null)
        {
            return null;
        }

        if (!_publicKeyCache.TryGetValue(kid, out var publicKey))
        {
            var keysBucket = await kv.GetStoreAsync(descriptor.IdentityPropagation.AssertionKeysBucket!).ConfigureAwait(false);
            var keyEntry = await keysBucket.GetEntryAsync<string>(SanitizeKey(kid)).ConfigureAwait(false);
            publicKey = keyEntry.Value!;
            _publicKeyCache[kid] = publicKey;
        }

        var claims = AssertionVerification.Verify(compactAssertionJwt, publicKey);
        if (claims is not null && claims.Expiry <= _timeProvider.GetUtcNow())
        {
            return null;
        }

        return claims;
    }

    /// <summary>
    /// Resolves the verified caller's OIDC token (doc 06 step 5), only valid when this
    /// subscriber's own profile has <c>autoAddSessionAccess</c> for the application.
    /// </summary>
    /// <param name="verifiedConnectionId">The verified assertion's <see cref="AuthCallout.Contracts.Seams.AssertionClaims.Id"/>.</param>
    /// <returns>The caller's raw OIDC token.</returns>
    public async Task<string> ResolveOidcTokenAsync(string verifiedConnectionId)
    {
        var tokensBucket = await kv.GetStoreAsync(descriptor.IdentityPropagation.OidcTokenBucket!).ConfigureAwait(false);
        var entry = await tokensBucket.GetEntryAsync<string>(verifiedConnectionId).ConfigureAwait(false);
        return entry.Value!;
    }

    private static string SanitizeKey(string segment) => AuthCallout.Contracts.Config.AppSlug.SanitizeKeySegment(segment);
}
