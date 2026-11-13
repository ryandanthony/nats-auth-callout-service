// -----------------------------------------------------------------------
// <copyright file="IdentityRecordWriter.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;
using AuthCallout.Nats.Callout;
using NATS.Client.JetStream;

namespace AuthCallout.Nats.Identity;

/// <summary>
/// Writes the OIDC-token record and the assertion record for one propagation-enabled grant.
/// Split out of <see cref="KvIdentityStoreWriter"/> purely to keep that class under the repo's
/// class-coupling and method-length analyzer limits; this is not an independent seam, it
/// always runs both writes for the same connection.
/// </summary>
/// <remarks>
/// The two writes are independent — each only needs the caller-generated
/// <see cref="IdentityRecordWrite.ConnectionId"/>, neither reads the other's result — so they
/// run concurrently (<see cref="Task.WhenAll(Task,Task)"/>) rather than sequentially: one round
/// trip's worth of latency instead of two. Both spans are started under an explicit parent
/// context captured before either starts, because
/// <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> always parents to whatever
/// is <see cref="Activity.Current"/> at the moment it is called — starting the second span
/// after the first is already current would nest it under the first (sibling stages) rather
/// than under their shared parent (see <see cref="CalloutActivitySource"/>).
/// </remarks>
internal static class IdentityRecordWriter
{
    /// <summary>Writes the token record and the assertion record concurrently.</summary>
    /// <param name="js">The JetStream context of the KV connection, for the assertion's raw TTL'd put.</param>
    /// <param name="write">The two records to write.</param>
    /// <param name="cancellationToken">Cancellation for both writes.</param>
    /// <returns>A task that completes when both writes have succeeded; throws if either fails.</returns>
    internal static async Task WriteBothAsync(INatsJSContext js, IdentityRecordWrite write, CancellationToken cancellationToken)
    {
        var parentContext = Activity.Current?.Context ?? default;

        var tokenWrite = CalloutActivitySource.TraceStageAsync(
            "kv.oidc_token.write",
            parentContext,
            () => write.TokenStore.CreateAsync(write.ConnectionId, write.RawOidcToken, write.Ttl, cancellationToken: cancellationToken).AsTask());

        // Last-writer-wins single put (see KvTtlPut). The self-lookup key legitimately
        // collides: the same token (same jti) can open many connections, and a reconnecting
        // client's sub is stable on the doc 07 fallback path, so a stale assertion for a
        // superseded connection must be replaced, never kept.
        var assertionWrite = CalloutActivitySource.TraceStageAsync(
            "kv.assertion.write",
            parentContext,
            () => KvTtlPut.PutAsync(js, write.AssertionBucket, write.AssertionKey, write.AssertionCompactJwt, write.Ttl, cancellationToken));

        await Task.WhenAll(tokenWrite, assertionWrite);
    }
}
