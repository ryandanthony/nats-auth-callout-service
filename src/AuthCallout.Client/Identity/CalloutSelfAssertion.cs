// -----------------------------------------------------------------------
// <copyright file="CalloutSelfAssertion.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Tokens;
using NATS.Client.KeyValueStore;

namespace AuthCallout.Client.Identity;

/// <summary>
/// Fetches the connecting client's own signed identity assertion (ADR 0010 §Resolution flow
/// step 3: "the client <c>GET</c>s its own entry ... keyed by the <c>jti</c> it already
/// knows"). Promoted from <c>dev/samples/Shared</c>.
/// </summary>
public static class CalloutSelfAssertion
{
    /// <summary>Fetches the caller's own signed assertion, keyed by its presented token's <c>jti</c> (falling back to <c>sub</c>, doc 07).</summary>
    /// <param name="kv">The connected client's KV context.</param>
    /// <param name="descriptor">The application's discovery descriptor (identity propagation must be enabled).</param>
    /// <param name="presentedOidcToken">The raw OIDC token this client connected with.</param>
    /// <param name="cancellationToken">Cancellation for the fetch.</param>
    /// <returns>The compact signed assertion JWT.</returns>
    /// <exception cref="InvalidOperationException">The token carries neither <c>jti</c> nor <c>sub</c>.</exception>
    public static async Task<string> FetchAsync(
        INatsKVContext kv, DiscoveryDescriptor descriptor, string presentedOidcToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kv);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentedOidcToken);

        var selfLookupKey = OidcTokenClaims.ReadJti(presentedOidcToken) ?? OidcTokenClaims.ReadSub(presentedOidcToken)
            ?? throw new InvalidOperationException("Presented token has neither 'jti' nor 'sub'; cannot self-lookup the assertion.");

        var assertionsBucket = await kv.GetStoreAsync(descriptor.IdentityPropagation.AssertionBucket!, cancellationToken).ConfigureAwait(false);
        var entry = await assertionsBucket.GetEntryAsync<string>(DeriveKey(selfLookupKey), cancellationToken: cancellationToken).ConfigureAwait(false);
        return entry.Value!;
    }

    private static string DeriveKey(string segment) => AuthCallout.Contracts.Config.AppSlug.DeriveKeySegment(segment);
}
