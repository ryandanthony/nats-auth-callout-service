// -----------------------------------------------------------------------
// <copyright file="DiscoveryPublisher.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using AuthCallout.Contracts.Config;
using NATS.Client.KeyValueStore;

namespace AuthCallout.Nats.Identity;

/// <summary>
/// Publishes each application's discovery descriptor (ADR 0012, doc 09 §2) to
/// <c>callout_apps_{slug}_discovery/descriptor</c>. Called at startup and whenever config
/// or the active assertion key changes (ADR 0012: "written at startup and whenever config
/// or the assertion key changes"). The payload shape matches
/// <c>schemas/discovery/descriptor/v1.json</c> exactly: the enabled variant carries the
/// header name, the three physical bucket names, and the current <c>kid</c>; the disabled
/// variant omits all four (ADR 0012 §2).
/// </summary>
public sealed class DiscoveryPublisher
{
    private const int DescriptorSchemaVersion = 1;
    private const string HeaderName = "Callout-Identity";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly INatsKVContext _kvContext;

    /// <summary>Initializes a new instance of the <see cref="DiscoveryPublisher"/> class.</summary>
    /// <param name="kvContext">The KV context to publish descriptors against (the callout's own connection).</param>
    public DiscoveryPublisher(INatsKVContext kvContext)
    {
        ArgumentNullException.ThrowIfNull(kvContext);
        _kvContext = kvContext;
    }

    /// <summary>
    /// Publishes every application's discovery descriptor. <paramref name="currentKid"/> is
    /// used for every propagation-enabled application's <c>currentKid</c> field (there is
    /// exactly one global active assertion key, ADR 0014); a null <paramref name="currentKid"/>
    /// (no assertion keyset configured) forces every application's descriptor into the
    /// disabled shape regardless of its own toggle, since there would be nothing to sign
    /// assertions with (defensive: config-load validation is expected to already reject a
    /// propagation-enabled application with no assertion keyset).
    /// </summary>
    /// <param name="routing">The routing aggregate to publish descriptors for.</param>
    /// <param name="currentKid">The active assertion signing key's <c>kid</c>, or null when no assertion keyset is configured.</param>
    /// <param name="cancellationToken">Cancellation for the publish attempt.</param>
    /// <returns>A task that completes when every application's descriptor has been published.</returns>
    public async Task PublishAsync(RoutingConfig routing, string? currentKid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routing);

        foreach (var application in routing.Applications)
        {
            var slug = application.Slug;
            var enabled = application.EffectiveIdentityPropagation.Enabled && currentKid is not null;
            var descriptor = BuildDescriptor(slug.Value, enabled, slug, currentKid);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(descriptor, SerializerOptions);

            var store = await _kvContext.CreateOrUpdateStoreAsync(new NatsKVConfig(slug.DiscoveryBucket), cancellationToken).ConfigureAwait(false);
            await store.PutAsync(AppSlug.DiscoveryDescriptorKey, bytes, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private static DiscoveryDescriptor BuildDescriptor(string appSlug, bool enabled, AppSlug slug, string? currentKid)
    {
        var propagation = enabled
            ? new IdentityPropagationDescriptor(true, HeaderName, slug.OidcTokensBucket, slug.AssertionsBucket, AppSlug.AssertionKeysBucket, currentKid)
            : new IdentityPropagationDescriptor(false, null, null, null, null, null);
        return new DiscoveryDescriptor(DescriptorSchemaVersion, appSlug, propagation);
    }

    /// <summary>The doc 09 §2 / <c>schemas/discovery/descriptor/v1.json</c> wire shape.</summary>
    private sealed record DiscoveryDescriptor(int SchemaVersion, string App, IdentityPropagationDescriptor IdentityPropagation);

    /// <summary>The nested <c>identityPropagation</c> object; all fields but <c>enabled</c> are omitted when disabled.</summary>
    private sealed record IdentityPropagationDescriptor(
        bool Enabled,
        string? Header,
        string? OidcTokenBucket,
        string? AssertionBucket,
        string? AssertionKeysBucket,
        string? CurrentKid);
}
