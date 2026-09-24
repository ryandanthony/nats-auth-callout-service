// -----------------------------------------------------------------------
// <copyright file="IdentityRecordWrite.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NATS.Client.KeyValueStore;

namespace AuthCallout.Nats.Identity;

/// <summary>The two records one propagation-enabled grant writes: the OIDC token and the signed identity assertion (doc 07 §Store identity).</summary>
/// <param name="TokenStore">The already-resolved OIDC-tokens bucket handle.</param>
/// <param name="ConnectionId">The generated connection id (the token record's key).</param>
/// <param name="RawOidcToken">The raw OIDC access token to store.</param>
/// <param name="AssertionBucket">The physical assertions bucket name.</param>
/// <param name="AssertionKey">The (already sanitized) self-lookup key.</param>
/// <param name="AssertionCompactJwt">The signed assertion to store.</param>
/// <param name="Ttl">The per-message TTL for both records (D17: <c>exp - now</c>).</param>
internal readonly record struct IdentityRecordWrite(
    INatsKVStore TokenStore,
    string ConnectionId,
    string RawOidcToken,
    string AssertionBucket,
    string AssertionKey,
    string AssertionCompactJwt,
    TimeSpan Ttl);
