// -----------------------------------------------------------------------
// <copyright file="PendingIdentityStoreWriter.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Host.Wiring;

/// <summary>
/// A temporary, fail-closed <see cref="IIdentityStoreWriter"/> used only until P08's real KV
/// implementation lands and this wiring is swapped to it (P08 is being built concurrently
/// with this packet per the swarm coordination plan; ground rule 6, "stub over blocking:
/// code against the Contracts interface"). Deliberately fails closed rather than silently
/// no-opping: every propagation-enabled grant is denied
/// (<see cref="DenyReason.IdentityStoreWriteFailed"/>, doc 03) instead of being replied to
/// without ever actually storing identity, and bucket-provisioning/keys-publication calls are
/// accepted but do nothing (safe: there is nothing to provision without a real writer). Not a
/// new abstraction — implements the frozen Contracts interface exactly as any other
/// implementation would, and is deleted the moment P08's <c>KvIdentityStoreWriter</c> (or
/// equivalent) is wired into <c>Program.cs</c> in its place.
/// </summary>
public sealed class PendingIdentityStoreWriter : IIdentityStoreWriter
{
    /// <inheritdoc />
    public Task ProvisionBucketsAsync(RoutingConfig routing, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<IdentityStoreWriteResult> WriteAsync(GrantedDecision grant, string rawOidcToken, CancellationToken cancellationToken) =>
        Task.FromResult(new IdentityStoreWriteResult(Succeeded: false, ConnectionId: null));

    /// <inheritdoc />
    public Task PublishKeysAndDiscoveryAsync(ActiveConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
}
