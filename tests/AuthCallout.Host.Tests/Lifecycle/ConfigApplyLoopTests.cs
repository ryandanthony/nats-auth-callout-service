// -----------------------------------------------------------------------
// <copyright file="ConfigApplyLoopTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Host.Tests.Lifecycle;

/// <summary>
/// Unit tests for <see cref="AuthCallout.Host.Lifecycle.ConfigApplyLoop"/>: ADR 0006 §Reload
/// / §Steady-state failure / §Cold start (accept swaps in, reject keeps last-good) and ADR
/// 0017 §2's clock-driven staleness table (Fresh / StaleDegraded / StaleUnready).
/// </summary>
public sealed class ConfigApplyLoopTests
{
    [Fact]
    public async Task A_valid_batch_swaps_in_and_reports_config_available()
    {
        await using var provider = new FakeRoutingConfigProvider();
        using var harness = ApplyLoopHarness.Create(provider);

        await provider.PushAsync(new RoutingConfigBatch(HostTestBuilders.EmptyRouting(), DateTimeOffset.UtcNow));
        await WaitUntilAsync(() => harness.ActiveConfigSource.Current is not null);

        Assert.NotNull(harness.ActiveConfigSource.Current);
        Assert.Equal(1, harness.ActiveConfigSource.Current!.Version);
        await harness.StopAsync();
    }

    [Fact]
    public async Task Each_accepted_batch_gets_a_monotonically_increasing_version()
    {
        await using var provider = new FakeRoutingConfigProvider();
        using var harness = ApplyLoopHarness.Create(provider);

        await provider.PushAsync(new RoutingConfigBatch(HostTestBuilders.EmptyRouting(), DateTimeOffset.UtcNow));
        await WaitUntilAsync(() => harness.ActiveConfigSource.Current?.Version == 1);
        await provider.PushAsync(new RoutingConfigBatch(HostTestBuilders.EmptyRouting(), DateTimeOffset.UtcNow));
        await WaitUntilAsync(() => harness.ActiveConfigSource.Current?.Version == 2);

        Assert.Equal(2, harness.ActiveConfigSource.Current!.Version);
        await harness.StopAsync();
    }

    [Fact]
    public async Task Bad_reload_with_last_good_keeps_last_good_and_reports_rejection()
    {
        await using var provider = new FakeRoutingConfigProvider();
        using var harness = ApplyLoopHarness.Create(provider);

        await provider.PushAsync(new RoutingConfigBatch(HostTestBuilders.EmptyRouting(), DateTimeOffset.UtcNow));
        await WaitUntilAsync(() => harness.ActiveConfigSource.Current is not null);
        var lastGood = harness.ActiveConfigSource.Current;

        await provider.PushAsync(new RoutingConfigBatch(DuplicateNameRouting(), DateTimeOffset.UtcNow));
        await WaitUntilAsync(() => harness.Sut.LastRejectionReason is not null);

        Assert.Same(lastGood, harness.ActiveConfigSource.Current);
        Assert.NotNull(harness.Sut.LastRejectionReason);
        await harness.StopAsync();
    }

    [Fact]
    public async Task Bad_first_batch_with_no_last_good_stays_without_a_config()
    {
        await using var provider = new FakeRoutingConfigProvider();
        using var harness = ApplyLoopHarness.Create(provider);

        await provider.PushAsync(new RoutingConfigBatch(DuplicateNameRouting(), DateTimeOffset.UtcNow));
        await WaitUntilAsync(() => harness.Sut.LastRejectionReason is not null);

        Assert.Null(harness.ActiveConfigSource.Current);
        Assert.Equal(ServiceState.Idle, harness.StateMachine.Current);
        await harness.StopAsync();
    }

    [Fact]
    public async Task OnApplied_callback_fires_once_per_accepted_batch_not_on_rejection()
    {
        var appliedCount = 0;
        await using var provider = new FakeRoutingConfigProvider();
        using var harness = ApplyLoopHarness.Create(
            provider, onApplied: (_, _) => IncrementAsync(ref appliedCount));

        await provider.PushAsync(new RoutingConfigBatch(HostTestBuilders.EmptyRouting(), DateTimeOffset.UtcNow));
        await WaitUntilAsync(() => harness.ActiveConfigSource.Current is not null);
        await provider.PushAsync(new RoutingConfigBatch(DuplicateNameRouting(), DateTimeOffset.UtcNow));
        await WaitUntilAsync(() => harness.Sut.LastRejectionReason is not null);
        await WaitUntilAsync(() => Volatile.Read(ref appliedCount) == 1, TimeSpan.FromSeconds(2));

        Assert.Equal(1, Volatile.Read(ref appliedCount));
        await harness.StopAsync();
    }

    [Fact]
    public async Task Freshness_is_Fresh_below_warnStaleness()
    {
        await using var provider = new FakeRoutingConfigProvider();
        using var harness = ApplyLoopHarness.CreateServing(provider, warnStaleness: TimeSpan.FromMinutes(5), maxStaleness: TimeSpan.FromMinutes(10));

        await provider.PushAsync(new RoutingConfigBatch(HostTestBuilders.EmptyRouting(), DateTimeOffset.UtcNow));
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        Assert.Equal(ConfigFreshness.Fresh, harness.StateMachine.Freshness);
        await harness.StopAsync();
    }

    [Fact]
    public async Task Freshness_becomes_StaleDegraded_past_warnStaleness_but_before_maxStaleness()
    {
        await using var provider = new FakeRoutingConfigProvider();
        using var harness = ApplyLoopHarness.CreateServing(provider, warnStaleness: TimeSpan.Zero, maxStaleness: TimeSpan.FromMinutes(10));

        await provider.PushAsync(new RoutingConfigBatch(HostTestBuilders.EmptyRouting(), DateTimeOffset.UtcNow));
        await WaitUntilAsync(() => harness.StateMachine.Freshness == ConfigFreshness.StaleDegraded, TimeSpan.FromSeconds(2));

        Assert.Equal(ConfigFreshness.StaleDegraded, harness.StateMachine.Freshness);
        await harness.StopAsync();
    }

    [Fact]
    public async Task Freshness_becomes_StaleUnready_past_maxStaleness_while_still_serving_last_good()
    {
        await using var provider = new FakeRoutingConfigProvider();
        using var harness = ApplyLoopHarness.CreateServing(provider, warnStaleness: TimeSpan.Zero, maxStaleness: TimeSpan.Zero);

        await provider.PushAsync(new RoutingConfigBatch(HostTestBuilders.EmptyRouting(), DateTimeOffset.UtcNow));
        await WaitUntilAsync(() => harness.StateMachine.Freshness == ConfigFreshness.StaleUnready, TimeSpan.FromSeconds(2));

        Assert.Equal(ConfigFreshness.StaleUnready, harness.StateMachine.Freshness);

        // Still serving last-good (ADR 0017: the hard bound is a readiness signal, not a kill switch).
        Assert.Equal(ServiceState.Serving, harness.StateMachine.Current);
        await harness.StopAsync();
    }

    private static Task IncrementAsync(ref int counter)
    {
        Interlocked.Increment(ref counter);
        return Task.CompletedTask;
    }

    private static RoutingConfig DuplicateNameRouting()
    {
        var issuer = new IssuerConfig(
            Authority: "https://id.example.com",
            JwksUri: null,
            JwksCache: DurationString.Parse("5m"),
            RefreshInterval: null,
            ValidateAudience: false,
            Audience: null,
            ClockSkew: DurationString.Parse("60s"),
            AllowedAlgorithms: ["RS256"]);
        var app = new Application(
            Name: "dup", TargetAccount: "ACCT", Issuer: issuer, Match: null, AnonymousProfile: null, Profiles: [], IdentityPropagation: null, Events: null);
        return new RoutingConfig([app, app]);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Condition was not satisfied within the timeout.");
    }
}
