// -----------------------------------------------------------------------
// <copyright file="BucketProvisioner.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using NATS.Client.KeyValueStore;

namespace AuthCallout.Nats.Identity;

/// <summary>
/// Ensures every JetStream KV bucket the identity stores need exists (D16: "the callout
/// creates buckets", doc 07 §Buckets). Called on every accepted config apply (P13); an
/// application removed from config simply stops being provisioned on the next apply — its
/// bucket is left orphaned rather than deleted (D16), never touched by this type.
/// </summary>
public sealed class BucketProvisioner
{
    /// <summary>
    /// How long the bucket retains delete/purge markers once per-message TTL expires an
    /// entry. This is also, per the installed <c>NATS.Client.KeyValueStore</c> client, the
    /// switch that turns on the underlying stream's <c>AllowMsgTTL</c> (any value greater
    /// than zero enables server-side per-message <c>Nats-TTL</c> header support, doc 08
    /// "JetStream per-message TTL", ADR 0010/D17); it is not a bucket-wide expiry ceiling —
    /// entries still expire at their own per-message TTL (the token's <c>exp - now</c>,
    /// D17: "no MaxAge backstop"). One minute is ample for callers to observe a delete
    /// marker without meaningfully extending how long a token record is discoverable after
    /// its own TTL fires.
    /// </summary>
    private static readonly TimeSpan MarkerRetention = TimeSpan.FromMinutes(1);

    private readonly INatsKVContext _kvContext;

    /// <summary>Initializes a new instance of the <see cref="BucketProvisioner"/> class.</summary>
    /// <param name="kvContext">The KV context to create/update buckets against (the callout's own connection).</param>
    public BucketProvisioner(INatsKVContext kvContext)
    {
        ArgumentNullException.ThrowIfNull(kvContext);
        _kvContext = kvContext;
    }

    /// <summary>
    /// Ensures the global assertion-keys bucket, every application's discovery bucket
    /// (regardless of the identity-propagation toggle, ADR 0012: the descriptor is always
    /// published), and — for propagation-enabled applications only — their OIDC-token and
    /// assertion buckets (ADR 0010/0011) all exist, with per-message TTL enabled and no
    /// bucket-wide <c>MaxAge</c> (D17). Uses create-or-update semantics, so it is safe to
    /// call on every config apply, not just the first.
    /// </summary>
    /// <param name="routing">The routing aggregate to provision buckets for.</param>
    /// <param name="cancellationToken">Cancellation for the provisioning attempt.</param>
    /// <returns>A task that completes when every bucket has been created or updated.</returns>
    public async Task ProvisionAsync(RoutingConfig routing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routing);

        await CreateOrUpdateAsync(AppSlug.AssertionKeysBucket, cancellationToken).ConfigureAwait(false);

        foreach (var application in routing.Applications)
        {
            var slug = application.Slug;
            await CreateOrUpdateAsync(slug.DiscoveryBucket, cancellationToken).ConfigureAwait(false);

            if (!application.EffectiveIdentityPropagation.Enabled)
            {
                continue;
            }

            await CreateOrUpdateAsync(slug.OidcTokensBucket, cancellationToken).ConfigureAwait(false);
            await CreateOrUpdateAsync(slug.AssertionsBucket, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CreateOrUpdateAsync(string bucket, CancellationToken cancellationToken)
    {
        var config = new NatsKVConfig(bucket) { LimitMarkerTTL = MarkerRetention };
        await _kvContext.CreateOrUpdateStoreAsync(config, cancellationToken).ConfigureAwait(false);
    }
}
