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
    /// <summary>Fetches and parses an application's discovery descriptor.</summary>
    /// <param name="kv">The connected client's KV context.</param>
    /// <param name="appName">The application's authored name (e.g. <c>notes</c>, <c>tasks</c>), slugified via <see cref="AppSlug"/>.</param>
    /// <returns>The parsed descriptor.</returns>
    public static async Task<DiscoveryDescriptor> FetchAsync(INatsKVContext kv, string appName)
    {
        ArgumentNullException.ThrowIfNull(kv);
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        var slug = AppSlug.From(appName);
        var bucket = await kv.GetStoreAsync(slug.DiscoveryBucket).ConfigureAwait(false);
        var entry = await bucket.GetEntryAsync<byte[]>(AppSlug.DiscoveryDescriptorKey).ConfigureAwait(false);
        return DiscoveryDescriptor.Parse(entry.Value!);
    }
}
