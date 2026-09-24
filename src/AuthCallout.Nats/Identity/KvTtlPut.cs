// -----------------------------------------------------------------------
// <copyright file="KvTtlPut.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace AuthCallout.Nats.Identity;

/// <summary>
/// A last-writer-wins JetStream KV put with a per-message TTL, in a single round trip.
/// <c>INatsKVStore.PutAsync</c> has no TTL overload in NATS.Net 3.2 (only create and purge take a
/// TTL), so this publishes the put directly: a KV put is, by definition, a JetStream publish to
/// <c>$KV.{bucket}.{key}</c>, and <c>Nats-TTL</c> is the header the KV client itself attaches
/// for its TTL'd create/purge operations (D17). The bucket must be provisioned with
/// <c>LimitMarkerTTL</c> (see <see cref="BucketProvisioner"/>), which is what enables the server
/// to honor <c>Nats-TTL</c>; otherwise the server rejects the publish and this throws.
/// </summary>
internal static class KvTtlPut
{
    /// <summary>The KV message header carrying a per-message TTL.</summary>
    internal const string TtlHeader = "Nats-TTL";

    /// <summary>Puts <paramref name="value"/> at <paramref name="key"/> in <paramref name="bucket"/>, replacing any existing entry, with a per-message TTL.</summary>
    /// <param name="js">The JetStream context of the KV connection.</param>
    /// <param name="bucket">The physical bucket name.</param>
    /// <param name="key">The (already validated/sanitized) KV key.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="ttl">The per-message TTL; must be positive.</param>
    /// <param name="cancellationToken">Cancellation for the publish.</param>
    /// <returns>A task that completes when the server has acknowledged the put.</returns>
    /// <exception cref="NatsJSApiException">The server rejected the put.</exception>
    internal static async Task PutAsync(INatsJSContext js, string bucket, string key, string value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var headers = new NatsHeaders { [TtlHeader] = FormatTtl(ttl) };
        var ack = await js.PublishAsync($"$KV.{bucket}.{key}", value, headers: headers, cancellationToken: cancellationToken);
        if (ack.Error is { } error)
        {
            throw new NatsJSApiException(error);
        }
    }

    /// <summary>
    /// Formats a TTL exactly as the NATS KV client formats its own <c>Nats-TTL</c> header (whole
    /// seconds, truncated, <c>s</c> suffix), so a record written here expires in step with records
    /// the client writes with the same TTL. Floors at <c>1s</c> so a sub-second remainder never
    /// produces <c>0s</c>.
    /// </summary>
    /// <param name="ttl">The TTL.</param>
    /// <returns>The header value, e.g. <c>420s</c>.</returns>
    internal static string FormatTtl(TimeSpan ttl) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1L, (long)ttl.TotalSeconds)}s");
}
