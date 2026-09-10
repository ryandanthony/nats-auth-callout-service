// -----------------------------------------------------------------------
// <copyright file="IdentityStoreWriterFactory.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using AuthCallout.Identity.Assertions;
using AuthCallout.Nats.Identity;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;

namespace AuthCallout.Host.Wiring;

/// <summary>
/// Selects the identity-store writer implementation (doc 07, P08). Extracted from
/// <see cref="ComponentGraph"/> so the KV/assertion types it depends on don't push that
/// factory over the repo's per-class coupling limit.
/// </summary>
public static class IdentityStoreWriterFactory
{
    /// <summary>
    /// Builds the real KV-backed <see cref="KvIdentityStoreWriter"/> (P08) when an assertion
    /// signing keyset is configured; otherwise returns the fail-closed
    /// <see cref="PendingIdentityStoreWriter"/> stand-in, because assertions cannot be signed
    /// without a keyset, so identity propagation is genuinely unavailable (a propagation-enabled
    /// grant is then denied rather than replied to without a stored assertion — ADR 0011/0012).
    /// </summary>
    /// <param name="natsConnection">The callout's service-user connection, used to open the KV context.</param>
    /// <param name="secrets">The resolved bootstrap secrets, carrying the assertion signing keyset.</param>
    /// <param name="secretResolver">Resolves each assertion key's public half for the keys-bucket publish.</param>
    /// <returns>The identity-store writer for this instance.</returns>
    // CA2000: the AssertionSigner (IDisposable) is handed to the returned KvIdentityStoreWriter,
    // which the DI container / AuthCalloutHostedService own for the process lifetime and dispose
    // on shutdown — the same "hand off a live disposable" pattern ComponentGraph documents.
#pragma warning disable CA2000
    public static IIdentityStoreWriter Create(
        NatsConnection natsConnection, ResolvedBootstrapSecrets secrets, ISecretResolver secretResolver)
    {
        ArgumentNullException.ThrowIfNull(natsConnection);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(secretResolver);

        if (secrets.AssertionKeys.Count == 0)
        {
            return new PendingIdentityStoreWriter();
        }

        var assertionSigner = new AssertionSigner(secrets.AssertionKeys);
        INatsKVContext kvContext = natsConnection.CreateKeyValueStoreContext();
        return new KvIdentityStoreWriter(
            kvContext, assertionSigner, secretResolver, onOrphanedBucket: static msg => Console.Error.WriteLine($"WARN: {msg}"));
    }
#pragma warning restore CA2000
}
