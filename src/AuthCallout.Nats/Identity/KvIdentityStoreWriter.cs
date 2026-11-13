// -----------------------------------------------------------------------
// <copyright file="KvIdentityStoreWriter.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Linq;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;
using NATS.Client.KeyValueStore;

namespace AuthCallout.Nats.Identity;

/// <summary>
/// The <see cref="IIdentityStoreWriter"/> implementation (ADR 0010, ADR 0011, ADR 0012, ADR
/// 0013, ADR 0014; doc 07 is authoritative for keys/values/readers/TTLs) over a real NATS
/// JetStream KV connection. Ties together <see cref="Identity.BucketProvisioner"/>,
/// <see cref="Identity.KeysPublisher"/>, and <see cref="Identity.DiscoveryPublisher"/> for
/// the provisioning/publish surfaces, and performs the per-authorization store write
/// itself. Deliberately takes an already-constructed <see cref="IAssertionSigner"/> rather
/// than a project reference to <c>AuthCallout.Identity</c> — the signer is Contracts-typed
/// (doc 08 §Project layout keeps <c>AuthCallout.Nats</c> and <c>AuthCallout.Identity</c>
/// siblings, neither referencing the other) — and resolves assertion-key public keys itself
/// via <see cref="ISecretResolver"/> + <c>NATS.NKeys</c> for the keys-bucket publish, since
/// that only needs the public half, not the full signer.
/// </summary>
public sealed class KvIdentityStoreWriter : IIdentityStoreWriter
{
    private readonly INatsKVContext _kvContext;
    private readonly IAssertionSigner _assertionSigner;
    private readonly ISecretResolver _secretResolver;
    private readonly TimeProvider _timeProvider;
    private readonly BucketProvisioner _bucketProvisioner;
    private readonly KeysPublisher _keysPublisher;
    private readonly DiscoveryPublisher _discoveryPublisher;
    private readonly KvStoreCache _stores;

    /// <summary>Initializes a new instance of the <see cref="KvIdentityStoreWriter"/> class.</summary>
    /// <param name="kvContext">The KV context every store/publish operation runs against (the callout's own connection).</param>
    /// <param name="assertionSigner">Signs each connection's identity assertion with the bootstrap keyset's active key.</param>
    /// <param name="secretResolver">Resolves each bootstrap assertion key's seed so its public half can be published (ADR 0011: "only the public key is ever published").</param>
    /// <param name="timeProvider">The time source for TTL computation (D17: <c>exp - now</c>). Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="onOrphanedBucket">Invoked with an orphaned per-application bucket's name during provisioning (D16). Optional; see <see cref="Identity.BucketProvisioner"/>.</param>
    public KvIdentityStoreWriter(
        INatsKVContext kvContext,
        IAssertionSigner assertionSigner,
        ISecretResolver secretResolver,
        TimeProvider? timeProvider = null,
        Action<string>? onOrphanedBucket = null)
    {
        ArgumentNullException.ThrowIfNull(kvContext);
        ArgumentNullException.ThrowIfNull(assertionSigner);
        ArgumentNullException.ThrowIfNull(secretResolver);

        _kvContext = kvContext;
        _assertionSigner = assertionSigner;
        _secretResolver = secretResolver;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _bucketProvisioner = new BucketProvisioner(kvContext, onOrphanedBucket);
        _keysPublisher = new KeysPublisher(kvContext);
        _discoveryPublisher = new DiscoveryPublisher(kvContext);
        _stores = new KvStoreCache(kvContext);
    }

    /// <inheritdoc />
    public async Task ProvisionBucketsAsync(RoutingConfig routing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routing);

        await _bucketProvisioner.ProvisionAsync(routing, cancellationToken);
        _stores.Clear();

        // Prewarm the token-store handle cache for every propagation-enabled application
        // (mirrors AuthCalloutHostedService's JWKS prewarm, doc 08). Without this, the very
        // first WriteAsync after any provision - including every fresh replica's first-ever
        // request - pays KvStoreCache's uncached $JS.API.STREAM.INFO round trip inline on the
        // auth hot path (observed as a callout_request_duration_ms outlier, ~300ms locally).
        // Best-effort: a prewarm failure must not stop this method from returning (the caller,
        // ConfigApplyLoopFactory.OnAppliedAsync, calls PublishKeysAndDiscoveryAsync right after
        // - that must still run even if a KV lookup here fails), and WriteAsync's own cache
        // miss on the next real request simply repeats the same lookup, so nothing is lost by
        // swallowing it here.
        var prewarmTasks = routing.Applications
            .Where(static app => app.EffectiveIdentityPropagation.Enabled)
            .Select(app => PrewarmTokenStoreAsync(app.Slug.OidcTokensBucket, cancellationToken));
        await Task.WhenAll(prewarmTasks);
    }

    private async Task PrewarmTokenStoreAsync(string oidcTokensBucket, CancellationToken cancellationToken)
    {
        try
        {
            await _stores.GetAsync(oidcTokensBucket, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallowed: see the ProvisionBucketsAsync comment above this call. The bucket was
            // just created/updated by _bucketProvisioner above, so a lookup failure here means
            // a transient KV/connection issue, not a missing bucket.
        }
    }

    /// <inheritdoc />
    public async Task<IdentityStoreWriteResult> WriteAsync(GrantedDecision grant, string rawOidcToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentException.ThrowIfNullOrEmpty(rawOidcToken);

        // doc 07 §Key parts / P08 packet task 4: propagation disabled, or neither jti nor
        // sub was available to key the client's self-lookup entry, skips both writes —
        // propagation is unavailable for the connection, but the grant still stands (the
        // caller never sees this as a failure).
        if (!grant.IdentityPropagationEnabled || grant.SelfLookupKey is null)
        {
            return new IdentityStoreWriteResult(Succeeded: true, ConnectionId: null);
        }

        var ttl = grant.Expiry - _timeProvider.GetUtcNow();
        if (ttl <= TimeSpan.Zero)
        {
            // Defensive: the resolution pipeline only ever produces a grant from a token
            // that has already passed exp validation (ADR 0016), so an already-expired
            // grant reaching here is an invariant violation, not a normal case. Fail
            // closed rather than write a record the server would reject or that would
            // expire before anyone could read it.
            return new IdentityStoreWriteResult(Succeeded: false, ConnectionId: null);
        }

        try
        {
            var slug = AppSlug.From(grant.Application!);
            var connectionId = ConnectionIdGenerator.NewId();
            var claims = new AssertionClaims(connectionId, grant.Principal!, slug.Value, grant.Issuer!, grant.Expiry);
            var signed = _assertionSigner.Sign(claims);
            var tokenStore = await _stores.GetAsync(slug.OidcTokensBucket, cancellationToken);
            var assertionKey = AppSlug.DeriveKeySegment(grant.SelfLookupKey);

            await IdentityRecordWriter.WriteBothAsync(
                _kvContext.JetStreamContext,
                new IdentityRecordWrite(tokenStore, connectionId, rawOidcToken, slug.AssertionsBucket, assertionKey, signed.CompactJwt, ttl),
                cancellationToken);

            return new IdentityStoreWriteResult(Succeeded: true, ConnectionId: connectionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // doc 03 §Store identity / P07's responder: any write failure here is reported
            // as a failure so the caller fails the grant closed, never as an exception the
            // responder would have to guard against separately.
            return new IdentityStoreWriteResult(Succeeded: false, ConnectionId: null);
        }
    }

    /// <inheritdoc />
    public async Task PublishKeysAndDiscoveryAsync(ActiveConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        var resolvedKeys = await AssertionKeyResolver.ResolveAsync(config.Bootstrap.AssertionKeys, _secretResolver, cancellationToken);
        await _keysPublisher.PublishAsync(resolvedKeys, cancellationToken);

        var currentKid = resolvedKeys.FirstOrDefault(static k => k.Active)?.Kid;
        await _discoveryPublisher.PublishAsync(config.Routing, currentKid, cancellationToken);
    }
}
