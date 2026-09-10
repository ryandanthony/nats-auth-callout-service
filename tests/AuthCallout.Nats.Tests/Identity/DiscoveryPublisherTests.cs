// -----------------------------------------------------------------------
// <copyright file="DiscoveryPublisherTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json.Nodes;
using AuthCallout.Nats.Identity;
using AuthCallout.Nats.Tests.Events;
using NATS.Client.KeyValueStore;
using NSubstitute;

namespace AuthCallout.Nats.Tests.Identity;

/// <summary>
/// Unit tests for <see cref="DiscoveryPublisher"/> (P08 ADR→test map: "Toggle off: no
/// token/assertion writes, discovery says enabled:false" ADR 0012; "Discovery payload
/// validates vs schema, both variants" doc 09). Asserts the published JSON against
/// <c>schemas/discovery/descriptor/v1.json</c> directly (same golden-schema pattern as
/// P09's <c>AuthOutcomeEventSchemaTests</c>).
/// </summary>
public sealed class DiscoveryPublisherTests
{
    [Fact]
    public async Task PublishAsync_PropagationEnabled_ValidatesAgainstSchema_HasAllEnabledFields()
    {
        var store = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(store);
        var publisher = new DiscoveryPublisher(kvContext);
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("notes", propagationEnabled: true));

        await publisher.PublishAsync(routing, currentKid: "2026-09-06a", CancellationToken.None);

        var payload = CapturedPayload(store);
        SchemaFixture.AssertValid("discovery/descriptor/v1.json", payload);
        Assert.Equal(1, payload!["schemaVersion"]!.GetValue<int>());
        Assert.Equal("notes", payload["app"]!.GetValue<string>());
        var propagation = payload["identityPropagation"]!;
        Assert.True(propagation["enabled"]!.GetValue<bool>());
        Assert.Equal("Callout-Identity", propagation["header"]!.GetValue<string>());
        Assert.Equal("callout_apps_notes_oidc_tokens", propagation["oidcTokenBucket"]!.GetValue<string>());
        Assert.Equal("callout_apps_notes_assertions", propagation["assertionBucket"]!.GetValue<string>());
        Assert.Equal("callout_assertion_keys", propagation["assertionKeysBucket"]!.GetValue<string>());
        Assert.Equal("2026-09-06a", propagation["currentKid"]!.GetValue<string>());
    }

    [Fact]
    public async Task PublishAsync_PropagationDisabled_ValidatesAgainstSchema_OmitsBucketFields()
    {
        var store = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(store);
        var publisher = new DiscoveryPublisher(kvContext);
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("tasks", propagationEnabled: false));

        await publisher.PublishAsync(routing, currentKid: "2026-09-06a", CancellationToken.None);

        var payload = CapturedPayload(store);
        SchemaFixture.AssertValid("discovery/descriptor/v1.json", payload);
        var propagation = payload!["identityPropagation"]!;
        Assert.False(propagation["enabled"]!.GetValue<bool>());
        Assert.Null(propagation["header"]);
        Assert.Null(propagation["oidcTokenBucket"]);
        Assert.Null(propagation["assertionBucket"]);
        Assert.Null(propagation["assertionKeysBucket"]);
        Assert.Null(propagation["currentKid"]);
    }

    [Fact]
    public async Task PublishAsync_NoAssertionKeyConfigured_ForcesDisabledDescriptor_EvenWhenAppTogglesEnabled()
    {
        // Defensive: an application configured with propagation enabled but no assertion
        // key resolved (e.g. an empty bootstrap keyset) has nothing to sign assertions
        // with, so the descriptor must not lie and advertise enabled:true.
        var store = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(store);
        var publisher = new DiscoveryPublisher(kvContext);
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("notes", propagationEnabled: true));

        await publisher.PublishAsync(routing, currentKid: null, CancellationToken.None);

        var payload = CapturedPayload(store);
        Assert.False(payload!["identityPropagation"]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task PublishAsync_WritesToTheDescriptorKey_InThatApplicationsDiscoveryBucket()
    {
        var store = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(store);
        var publisher = new DiscoveryPublisher(kvContext);
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("notes", propagationEnabled: true));

        await publisher.PublishAsync(routing, currentKid: "kid-1", CancellationToken.None);

        await kvContext.Received(1).CreateOrUpdateStoreAsync(
            Arg.Is<NatsKVConfig>(c => c.Bucket == "callout_apps_notes_discovery"), Arg.Any<CancellationToken>());
        await store.Received(1).PutAsync("descriptor", Arg.Any<byte[]>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    private static INatsKVContext ContextReturning(INatsKVStore store)
    {
        var kvContext = Substitute.For<INatsKVContext>();
        kvContext.CreateOrUpdateStoreAsync(Arg.Any<NatsKVConfig>(), Arg.Any<CancellationToken>()).Returns(store);
        return kvContext;
    }

    private static JsonNode? CapturedPayload(INatsKVStore store)
    {
        var call = store.ReceivedCalls().Single(c => string.Equals(c.GetMethodInfo().Name, nameof(INatsKVStore.PutAsync), StringComparison.Ordinal));
        var bytes = (byte[])call.GetArguments()[1]!;
        return JsonNode.Parse(bytes);
    }
}
