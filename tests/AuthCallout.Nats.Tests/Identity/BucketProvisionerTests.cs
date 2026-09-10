// -----------------------------------------------------------------------
// <copyright file="BucketProvisionerTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Nats.Identity;
using NATS.Client.KeyValueStore;
using NSubstitute;

namespace AuthCallout.Nats.Tests.Identity;

/// <summary>
/// Unit tests for <see cref="BucketProvisioner"/> (P08 ADR→test map: "Provisioner
/// idempotent; physical bucket/key names for every logical path", D16, ADR 0013). Asserts
/// against a substituted <see cref="INatsKVContext"/>: which buckets get created/updated,
/// with per-message TTL enabled and no bucket-wide <c>MaxAge</c>, and that a propagation-
/// disabled application still gets its discovery bucket but not its token/assertion
/// buckets.
/// </summary>
public sealed class BucketProvisionerTests
{
    [Fact]
    public async Task ProvisionAsync_PropagationEnabledApp_CreatesAllFourBucketsWithCorrectNames()
    {
        var kvContext = Substitute.For<INatsKVContext>();
        var provisioner = new BucketProvisioner(kvContext);
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("notes", propagationEnabled: true));

        await provisioner.ProvisionAsync(routing, CancellationToken.None);

        var createdBuckets = CapturedBucketNames(kvContext);
        Assert.Contains(AppSlug.AssertionKeysBucket, createdBuckets);
        Assert.Contains("callout_apps_notes_discovery", createdBuckets);
        Assert.Contains("callout_apps_notes_oidc_tokens", createdBuckets);
        Assert.Contains("callout_apps_notes_assertions", createdBuckets);
    }

    [Fact]
    public async Task ProvisionAsync_PropagationDisabledApp_CreatesDiscoveryBucketOnly_NotTokenOrAssertionBuckets()
    {
        var kvContext = Substitute.For<INatsKVContext>();
        var provisioner = new BucketProvisioner(kvContext);
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("tasks", propagationEnabled: false));

        await provisioner.ProvisionAsync(routing, CancellationToken.None);

        var createdBuckets = CapturedBucketNames(kvContext);
        Assert.Contains("callout_apps_tasks_discovery", createdBuckets);
        Assert.DoesNotContain("callout_apps_tasks_oidc_tokens", createdBuckets);
        Assert.DoesNotContain("callout_apps_tasks_assertions", createdBuckets);
    }

    [Fact]
    public async Task ProvisionAsync_EveryBucket_EnablesPerMessageTtl_NoBucketWideMaxAge()
    {
        // D17: "no MaxAge backstop" — only per-message TTL (LimitMarkerTTL > 0, which is
        // what the installed NATS.Client.KeyValueStore client uses to switch on the
        // underlying stream's AllowMsgTTL) is set; NatsKVConfig.MaxAge stays at its default
        // (TimeSpan.Zero, meaning "no bucket-wide ceiling").
        var kvContext = Substitute.For<INatsKVContext>();
        var provisioner = new BucketProvisioner(kvContext);
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("notes", propagationEnabled: true));

        await provisioner.ProvisionAsync(routing, CancellationToken.None);

        var configs = CapturedConfigs(kvContext);
        Assert.NotEmpty(configs);
        Assert.All(configs, config => Assert.True(config.LimitMarkerTTL > TimeSpan.Zero));
        Assert.All(configs, config => Assert.Equal(TimeSpan.Zero, config.MaxAge));
    }

    [Fact]
    public async Task ProvisionAsync_NoApplications_StillCreatesGlobalAssertionKeysBucket()
    {
        var kvContext = Substitute.For<INatsKVContext>();
        var provisioner = new BucketProvisioner(kvContext);

        await provisioner.ProvisionAsync(RoutingConfig.Empty, CancellationToken.None);

        var createdBuckets = CapturedBucketNames(kvContext);
        Assert.Contains(AppSlug.AssertionKeysBucket, createdBuckets);
        Assert.Single(createdBuckets);
    }

    [Fact]
    public async Task ProvisionAsync_CalledTwice_UsesCreateOrUpdateSemantics_BothCallsSucceed()
    {
        // D16: "Idempotent (create-or-update semantics)" — calling twice (e.g. two config
        // applies) must not throw or duplicate anything; CreateOrUpdateStoreAsync is the
        // NATS client's own idempotent primitive, so this just proves the provisioner
        // relies on it every time rather than a one-shot Create.
        var kvContext = Substitute.For<INatsKVContext>();
        var provisioner = new BucketProvisioner(kvContext);
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("notes", propagationEnabled: true));

        await provisioner.ProvisionAsync(routing, CancellationToken.None);
        await provisioner.ProvisionAsync(routing, CancellationToken.None);

        await kvContext.Received(2).CreateOrUpdateStoreAsync(
            Arg.Is<NatsKVConfig>(c => c.Bucket == "callout_apps_notes_oidc_tokens"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionAsync_ExistingPerAppBucketForRemovedApplication_ReportsOrphan_DoesNotDeleteIt()
    {
        // D16: "Removal: leave orphaned, log + warning metric." "old-app" no longer appears
        // in the routing config, but its token bucket still exists in the KV store.
        var kvContext = Substitute.For<INatsKVContext>();
        kvContext.GetBucketNamesAsync(Arg.Any<CancellationToken>()).Returns(ToAsyncEnumerable(["callout_apps_old_app_oidc_tokens"]));
        var reported = new List<string>();
        var provisioner = new BucketProvisioner(kvContext, onOrphanedBucket: reported.Add);
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("notes", propagationEnabled: true));

        await provisioner.ProvisionAsync(routing, CancellationToken.None);

        Assert.Contains("callout_apps_old_app_oidc_tokens", reported);
        await kvContext.DidNotReceiveWithAnyArgs().DeleteStoreAsync(default!, default);
    }

    [Fact]
    public async Task ProvisionAsync_ExistingBucketForCurrentlyConfiguredApp_IsNotReportedAsOrphan()
    {
        var kvContext = Substitute.For<INatsKVContext>();
        kvContext.GetBucketNamesAsync(Arg.Any<CancellationToken>()).Returns(ToAsyncEnumerable(["callout_apps_notes_oidc_tokens", AppSlug.AssertionKeysBucket]));
        var reported = new List<string>();
        var provisioner = new BucketProvisioner(kvContext, onOrphanedBucket: reported.Add);
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("notes", propagationEnabled: true));

        await provisioner.ProvisionAsync(routing, CancellationToken.None);

        Assert.Empty(reported);
    }

    [Fact]
    public async Task ProvisionAsync_OrphanedBucket_IncrementsOrphanedBucketsCounter()
    {
        var kvContext = Substitute.For<INatsKVContext>();
        kvContext.GetBucketNamesAsync(Arg.Any<CancellationToken>()).Returns(ToAsyncEnumerable(["callout_apps_old_app_discovery"]));
        var provisioner = new BucketProvisioner(kvContext);
        var routing = RoutingConfig.Empty;

        long total = 0;
        using (SubscribeToOrphanedBucketsCounter(measured => total += measured))
        {
            await provisioner.ProvisionAsync(routing, CancellationToken.None);
        }

        Assert.True(total > 0, $"Expected {BucketProvisionerMetrics.OrphanedBucketsCounterName} to record at least one measurement (recorded={total}).");
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            yield return value;
        }

        await Task.CompletedTask;
    }

    private static System.Diagnostics.Metrics.MeterListener SubscribeToOrphanedBucketsCounter(Action<long> onMeasured)
    {
        // Same MeterListener approach as EventEmitterTests.SubscribeToPublishFailuresCounter.
        var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (string.Equals(instrument.Meter.Name, BucketProvisionerMetrics.MeterName, StringComparison.Ordinal)
                && string.Equals(instrument.Name, BucketProvisionerMetrics.OrphanedBucketsCounterName, StringComparison.Ordinal))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => onMeasured(measurement));
        listener.Start();
        return listener;
    }

    private static List<string> CapturedBucketNames(INatsKVContext kvContext) =>
        CapturedConfigs(kvContext).ConvertAll(c => c.Bucket);

    private static List<NatsKVConfig> CapturedConfigs(INatsKVContext kvContext) =>
        kvContext.ReceivedCalls()
            .Where(call => string.Equals(call.GetMethodInfo().Name, nameof(INatsKVContext.CreateOrUpdateStoreAsync), StringComparison.Ordinal))
            .Select(call => (NatsKVConfig)call.GetArguments()[0]!)
            .ToList();
}
