// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityResolver.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Tokens;
using AuthCallout.Contracts.Seams;
using AuthCallout.Identity.Assertions;
using NATS.Client.KeyValueStore;

namespace AuthCallout.Client.Identity;

/// <summary>
/// The subscriber-side half of doc 06 "How the subscriber resolves the caller's identity":
/// verify the <c>Callout-Identity</c> header's signature against the keys bucket (cached per
/// <c>kid</c>), then, only once verified, optionally resolve the caller's OIDC token from the
/// token-store bucket (requires the subscribing profile's <c>autoAddSessionAccess</c>).
/// Promoted from <c>dev/samples/Shared</c>.
/// </summary>
public sealed class CalloutIdentityResolver : IDisposable
{
    private readonly INatsKVContext _kv;
    private readonly DiscoveryDescriptor _descriptor;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, string> _publicKeyCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    /// <summary>Initializes a new instance of the <see cref="CalloutIdentityResolver"/> class.</summary>
    /// <param name="kv">The service's own connected KV context.</param>
    /// <param name="descriptor">The application's discovery descriptor.</param>
    /// <param name="timeProvider">The time provider for expiry checks; defaults to <see cref="TimeProvider.System"/>.</param>
    public CalloutIdentityResolver(INatsKVContext kv, DiscoveryDescriptor descriptor, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(kv);
        ArgumentNullException.ThrowIfNull(descriptor);
        _kv = kv;
        _descriptor = descriptor;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Verifies a <c>Callout-Identity</c> header value's signature and returns the verified
    /// claims, or null if verification fails for any reason (missing <c>kid</c>, unknown key,
    /// bad signature, expired). Never throws on a malformed/forged header — a message with no
    /// valid header conveys no identity (ADR 0010) and callers should treat a null result as
    /// "unverified", not as an error.
    /// </summary>
    /// <param name="compactAssertionJwt">The raw header value.</param>
    /// <param name="cancellationToken">Cancellation for the key-bucket lookup on a cache miss.</param>
    /// <returns>The verified claims, or null.</returns>
    public async Task<AssertionClaims?> VerifyAsync(string compactAssertionJwt, CancellationToken cancellationToken = default)
    {
        var kid = OidcTokenClaims.ReadJwtHeaderKid(compactAssertionJwt);
        if (kid is null)
        {
            return null;
        }

        var publicKey = await GetOrFetchPublicKeyAsync(kid, cancellationToken).ConfigureAwait(false);
        if (publicKey is null)
        {
            return null;
        }

        var claims = AssertionVerifier.Verify(compactAssertionJwt, publicKey);
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
    /// <param name="verifiedConnectionId">The verified assertion's <see cref="AssertionClaims.Id"/>.</param>
    /// <param name="cancellationToken">Cancellation for the token-bucket lookup.</param>
    /// <returns>The caller's raw OIDC token.</returns>
    public async Task<string> ResolveOidcTokenAsync(string verifiedConnectionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedConnectionId);
        var tokensBucket = await _kv.GetStoreAsync(_descriptor.IdentityPropagation.OidcTokenBucket!, cancellationToken).ConfigureAwait(false);
        var entry = await tokensBucket.GetEntryAsync<string>(verifiedConnectionId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return entry.Value!;
    }

    /// <inheritdoc />
    public void Dispose() => _cacheLock.Dispose();

    private async Task<string?> GetOrFetchPublicKeyAsync(string kid, CancellationToken cancellationToken)
    {
        if (_publicKeyCache.TryGetValue(kid, out var cached))
        {
            return cached;
        }

        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await FetchAndCachePublicKeyAsync(kid, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<string?> FetchAndCachePublicKeyAsync(string kid, CancellationToken cancellationToken)
    {
        if (_publicKeyCache.TryGetValue(kid, out var cached))
        {
            return cached;
        }

        if (_descriptor.IdentityPropagation.AssertionKeysBucket is null)
        {
            return null;
        }

        try
        {
            var keysBucket = await _kv.GetStoreAsync(_descriptor.IdentityPropagation.AssertionKeysBucket, cancellationToken).ConfigureAwait(false);
            var keyEntry = await keysBucket.GetEntryAsync<string>(SanitizeKey(kid), cancellationToken: cancellationToken).ConfigureAwait(false);
            var publicKey = keyEntry.Value!;
            _publicKeyCache[kid] = publicKey;
            return publicKey;
        }
        catch (Exception)
        {
            // Unknown kid / key not found / KV error: the assertion cannot be trusted
            // (ADR 0010: never throw on forged input, treat as unverified).
            return null;
        }
    }

    private static string SanitizeKey(string segment) => AuthCallout.Contracts.Config.AppSlug.SanitizeKeySegment(segment);
}
