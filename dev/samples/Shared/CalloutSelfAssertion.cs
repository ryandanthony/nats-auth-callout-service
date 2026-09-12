// -----------------------------------------------------------------------
// <copyright file="CalloutSelfAssertion.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NATS.Client.KeyValueStore;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// Fetches the connecting client's own signed identity assertion (ADR 0010 §Resolution flow
/// step 3: "the client `GET`s **its own** entry ... keyed by the `jti` it already knows").
/// </summary>
public static class CalloutSelfAssertion
{
    /// <summary>Fetches the caller's own signed assertion, keyed by its presented token's <c>jti</c> (falling back to <c>sub</c>, doc 07).</summary>
    /// <param name="kv">The connected client's KV context.</param>
    /// <param name="descriptor">The application's discovery descriptor (identity propagation must be enabled).</param>
    /// <param name="presentedOidcToken">The raw OIDC token this client connected with.</param>
    /// <returns>The compact signed assertion JWT.</returns>
    /// <exception cref="InvalidOperationException">The token carries neither <c>jti</c> nor <c>sub</c>.</exception>
    public static async Task<string> FetchAsync(INatsKVContext kv, DiscoveryDescriptor descriptor, string presentedOidcToken)
    {
        ArgumentNullException.ThrowIfNull(kv);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentedOidcToken);

        var selfLookupKey = OidcTokenClaims.ReadJti(presentedOidcToken) ?? OidcTokenClaims.ReadSub(presentedOidcToken)
            ?? throw new InvalidOperationException("Presented token has neither 'jti' nor 'sub'; cannot self-lookup the assertion.");

        var assertionsBucket = await kv.GetStoreAsync(descriptor.IdentityPropagation.AssertionBucket!);
        var entry = await assertionsBucket.GetEntryAsync<string>(DeriveKey(selfLookupKey));
        return entry.Value!;
    }

    private static string DeriveKey(string segment) => AuthCallout.Contracts.Config.AppSlug.DeriveKeySegment(segment);
}
