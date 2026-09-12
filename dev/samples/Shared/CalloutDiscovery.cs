// -----------------------------------------------------------------------
// <copyright file="CalloutDiscovery.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using NATS.Client.KeyValueStore;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// Fetches an application's discovery descriptor (doc 06: "Both client and subscriber first
/// <c>GET callout.apps.{app-slug}.discovery</c> (cached)"). The bucket name comes from
/// <see cref="AppSlug"/> (ADR 0013's single source of truth for the naming rule, shared with
/// the callout itself), never re-derived by string formatting here; every other bucket/header
/// name this sample uses comes from the descriptor's own fields.
/// </summary>
public static class CalloutDiscovery
{
    /// <summary>
    /// How many times to retry a "bucket not provisioned yet" failure before giving up.
    /// The callout's config-apply loop applies a config batch (and admits new connections
    /// under it) *before* awaiting the bucket-provisioning side effect - deliberately
    /// fire-and-forget, so provisioning never blocks picking up the next batch. A client
    /// that authorizes in the same instant a fresh config lands can race a KV bucket that
    /// hasn't been created yet; a real consumer needs the same short retry this sample
    /// does, not just a demo one-off.
    /// </summary>
    private const int MaxAttempts = 20;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Fetches and parses an application's discovery descriptor.</summary>
    /// <param name="kv">The connected client's KV context.</param>
    /// <param name="appName">The application's authored name (e.g. <c>notes</c>, <c>tasks</c>), slugified via <see cref="AppSlug"/>.</param>
    /// <returns>The parsed descriptor.</returns>
    public static async Task<DiscoveryDescriptor> FetchAsync(INatsKVContext kv, string appName)
    {
        ArgumentNullException.ThrowIfNull(kv);
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        var slug = AppSlug.From(appName);
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var bucket = await kv.GetStoreAsync(slug.DiscoveryBucket);
                var entry = await bucket.GetEntryAsync<byte[]>(AppSlug.DiscoveryDescriptorKey);
                return DiscoveryDescriptor.Parse(entry.Value!);
            }
            catch (Exception) when (attempt < MaxAttempts)
            {
                // Bucket/entry not provisioned yet (see MaxAttempts' doc comment); retry with
                // a short fixed delay rather than surfacing a demo-breaking race.
                await Task.Delay(RetryDelay);
            }
        }

        // Unreachable: attempt == MaxAttempts always either returns or lets the final
        // exception propagate uncaught (the when-clause is false on the last iteration).
        throw new InvalidOperationException($"Discovery fetch for '{appName}' failed after {MaxAttempts} attempts.");
    }
}
