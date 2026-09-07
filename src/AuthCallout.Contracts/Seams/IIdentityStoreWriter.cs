// -----------------------------------------------------------------------
// <copyright file="IIdentityStoreWriter.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// Ensures KV buckets exist and performs the per-authorization identity-store writes (ADR
/// 0010, ADR 0011, ADR 0012, ADR 0013, ADR 0014; doc 07 is authoritative for keys/values/
/// readers/TTLs). Implemented by <c>AuthCallout.Nats</c> (P08); consumed by the callout
/// responder (P07) before it replies, and by the config-apply loop (P13) for bucket
/// provisioning and discovery/keys reconciliation on every accepted config change.
/// </summary>
public interface IIdentityStoreWriter
{
    /// <summary>
    /// Ensures every KV bucket a currently-configured, propagation-enabled application
    /// needs exists (D16: the callout creates buckets), plus the global assertion-keys
    /// bucket and every application's discovery bucket. Idempotent (create-or-update); a
    /// bucket for an application removed from config is left orphaned (D16), not deleted.
    /// Called on every accepted config apply (P13).
    /// </summary>
    /// <param name="routing">The routing aggregate to provision buckets for.</param>
    /// <param name="cancellationToken">Cancellation for the provisioning attempt.</param>
    /// <returns>A task that completes when provisioning has been attempted for every relevant bucket.</returns>
    Task ProvisionBucketsAsync(RoutingConfig routing, CancellationToken cancellationToken);

    /// <summary>
    /// Performs the doc 03 §Store identity step for one successful, non-anonymous grant:
    /// generates a connection id, writes the raw OIDC token, signs and writes the identity
    /// assertion. A no-op (returning a successful result with a null
    /// <see cref="IdentityStoreWriteResult.ConnectionId"/>) when the application has
    /// identity propagation disabled. Must complete before the auth response is sent
    /// (doc 03: "before replying").
    /// </summary>
    /// <param name="grant">The successful, non-anonymous grant to store identity for.</param>
    /// <param name="rawOidcToken">The client's raw OIDC access token to store.</param>
    /// <param name="cancellationToken">Cancellation for the write attempt.</param>
    /// <returns>The outcome of the write (or no-op skip).</returns>
    Task<IdentityStoreWriteResult> WriteAsync(GrantedDecision grant, string rawOidcToken, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes every assertion signing keyset public key and the <c>current</c> pointer
    /// (ADR 0011, ADR 0014), and every application's discovery descriptor (ADR 0012),
    /// reflecting the given config. Called at startup and whenever config or the assertion
    /// keyset changes.
    /// </summary>
    /// <param name="config">The active config snapshot to publish keys and discovery descriptors for.</param>
    /// <param name="cancellationToken">Cancellation for the publish attempt.</param>
    /// <returns>A task that completes when the publish has been attempted.</returns>
    Task PublishKeysAndDiscoveryAsync(ActiveConfig config, CancellationToken cancellationToken);
}
