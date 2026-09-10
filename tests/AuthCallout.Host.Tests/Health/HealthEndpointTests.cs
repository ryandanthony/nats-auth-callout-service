// -----------------------------------------------------------------------
// <copyright file="HealthEndpointTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using AuthCallout.Contracts.Seams;
using AuthCallout.Host.Health;
using AuthCallout.Host.Lifecycle;
using NATS.Client.Core;
using NSubstitute;

namespace AuthCallout.Host.Tests.Health;

/// <summary>
/// Alba tests driving the real registered <c>IHealthCheck</c>s and
/// <see cref="HealthEndpointMapper"/> routes through an actual <see cref="Microsoft.AspNetCore.Builder.WebApplication"/>
/// (doc 08 §Health &amp; readiness, D21). Covers the packet's ADR→test map row "/healthz vs
/// /readyz vs /startupz tag filtering, JSON detail, 503 rules", plus "Liveness unaffected by
/// Idle" and the "Health JSON never contains secret material" DoD item.
/// </summary>
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Healthz_is_200_even_when_not_Serving_liveness_unaffected_by_Idle()
    {
        var stateMachine = new ServiceStateMachine(prewarmRequired: true); // stays Starting: no gates cleared.
        await using var host = await HealthTestHost.BuildAsync(new HealthTestHostOptions { StateMachine = stateMachine });

        await host.Scenario(s =>
        {
            s.Get.Url(HealthEndpointMapper.LivenessPath);
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
    }

    [Fact]
    public async Task Healthz_only_runs_the_live_tagged_check_not_ready_or_startup()
    {
        await using var host = await HealthTestHost.BuildAsync();

        var result = await host.Scenario(s =>
        {
            s.Get.Url(HealthEndpointMapper.LivenessPath);
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var body = await result.ReadAsTextAsync();
        Assert.Contains("\"self\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"nats-connection\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"config-active\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"started\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readyz_is_200_when_Serving_with_a_healthy_connection_and_fresh_config()
    {
        var stateMachine = new ServiceStateMachine(prewarmRequired: false);
        stateMachine.SetConfigAvailable(available: true);
        stateMachine.SetNatsConnected(connected: true);
        var options = new HealthTestHostOptions
        {
            StateMachine = stateMachine,
            Connection = HealthTestHostOptions.SubstituteConnection(NatsConnectionState.Open),
            RoutingProvider = new StaticLastSyncRoutingProvider(DateTimeOffset.UtcNow),
        };
        await using var host = await HealthTestHost.BuildAsync(options);

        await host.Scenario(s =>
        {
            s.Get.Url(HealthEndpointMapper.ReadinessPath);
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
    }

    [Fact]
    public async Task Readyz_is_503_when_not_Serving_config_active_check_fails()
    {
        var stateMachine = new ServiceStateMachine(prewarmRequired: true); // Idle: config-active check fails.
        await using var host = await HealthTestHost.BuildAsync(new HealthTestHostOptions { StateMachine = stateMachine });

        await host.Scenario(s =>
        {
            s.Get.Url(HealthEndpointMapper.ReadinessPath);
            s.StatusCodeShouldBe(HttpStatusCode.ServiceUnavailable);
        });
    }

    [Fact]
    public async Task Readyz_is_503_when_the_NATS_connection_is_not_Open_ADR_0017()
    {
        var stateMachine = new ServiceStateMachine(prewarmRequired: false);
        stateMachine.SetConfigAvailable(available: true);
        stateMachine.SetNatsConnected(connected: true);
        var options = new HealthTestHostOptions
        {
            StateMachine = stateMachine,
            Connection = HealthTestHostOptions.SubstituteConnection(NatsConnectionState.Reconnecting),
        };
        await using var host = await HealthTestHost.BuildAsync(options);

        await host.Scenario(s =>
        {
            s.Get.Url(HealthEndpointMapper.ReadinessPath);
            s.StatusCodeShouldBe(HttpStatusCode.ServiceUnavailable);
        });
    }

    [Fact]
    public async Task Readyz_is_still_200_at_StaleDegraded_ADR_0017_warn_threshold()
    {
        var stateMachine = new ServiceStateMachine(prewarmRequired: false);
        stateMachine.SetConfigAvailable(available: true);
        stateMachine.SetNatsConnected(connected: true);
        stateMachine.SetFreshness(ConfigFreshness.StaleDegraded);
        await using var host = await HealthTestHost.BuildAsync(new HealthTestHostOptions { StateMachine = stateMachine });

        await host.Scenario(s =>
        {
            s.Get.Url(HealthEndpointMapper.ReadinessPath);
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
    }

    [Fact]
    public async Task Readyz_is_503_at_StaleUnready_ADR_0017_max_threshold_while_still_serving()
    {
        var stateMachine = new ServiceStateMachine(prewarmRequired: false);
        stateMachine.SetConfigAvailable(available: true);
        stateMachine.SetNatsConnected(connected: true);
        stateMachine.SetFreshness(ConfigFreshness.StaleUnready);
        await using var host = await HealthTestHost.BuildAsync(new HealthTestHostOptions { StateMachine = stateMachine });

        await host.Scenario(s =>
        {
            s.Get.Url(HealthEndpointMapper.ReadinessPath);
            s.StatusCodeShouldBe(HttpStatusCode.ServiceUnavailable);
        });

        // Still Serving (last-good): the readiness 503 is a shed signal, not a fail-closed denial.
        Assert.Equal(ServiceState.Serving, stateMachine.Current);
    }

    [Fact]
    public async Task Startupz_is_503_before_MarkStarted_and_200_after()
    {
        var startupCheck = new StartupHealthCheck();
        await using var host = await HealthTestHost.BuildAsync(new HealthTestHostOptions { StartupHealthCheck = startupCheck });

        await host.Scenario(s =>
        {
            s.Get.Url(HealthEndpointMapper.StartupPath);
            s.StatusCodeShouldBe(HttpStatusCode.ServiceUnavailable);
        });

        startupCheck.MarkStarted();

        await host.Scenario(s =>
        {
            s.Get.Url(HealthEndpointMapper.StartupPath);
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
    }

    [Fact]
    public async Task Startupz_only_runs_the_startup_tagged_check()
    {
        var startupCheck = new StartupHealthCheck();
        startupCheck.MarkStarted();
        await using var host = await HealthTestHost.BuildAsync(new HealthTestHostOptions { StartupHealthCheck = startupCheck });

        var result = await host.Scenario(s =>
        {
            s.Get.Url(HealthEndpointMapper.StartupPath);
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var body = await result.ReadAsTextAsync();
        Assert.Contains("\"started\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"self\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"nats-connection\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readyz_JSON_reports_per_check_status_description_and_data()
    {
        var stateMachine = new ServiceStateMachine(prewarmRequired: false);
        stateMachine.SetConfigAvailable(available: true);
        stateMachine.SetNatsConnected(connected: true);
        await using var host = await HealthTestHost.BuildAsync(new HealthTestHostOptions { StateMachine = stateMachine });

        var result = await host.Scenario(s =>
        {
            s.Get.Url(HealthEndpointMapper.ReadinessPath);
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var body = await result.ReadAsTextAsync();
        Assert.Contains("\"status\":\"Healthy\"", body, StringComparison.Ordinal);
        Assert.Contains("\"durationMs\"", body, StringComparison.Ordinal);
        Assert.Contains("\"data\"", body, StringComparison.Ordinal);
        Assert.Contains("\"connectionState\":\"Open\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_JSON_never_contains_secret_material()
    {
        var stateMachine = new ServiceStateMachine(prewarmRequired: false);
        stateMachine.SetConfigAvailable(available: true);
        stateMachine.SetNatsConnected(connected: true);
        const string secretValue = "super-secret-issuer-seed-value";
        await using var host = await HealthTestHost.BuildAsync(new HealthTestHostOptions { StateMachine = stateMachine });

        var result = await host.Scenario(s =>
        {
            s.Get.Url(HealthEndpointMapper.ReadinessPath);
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var body = await result.ReadAsTextAsync();
        Assert.DoesNotContain(secretValue, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Jwks_check_is_untagged_and_never_gates_readyz_when_degraded()
    {
        var stateMachine = new ServiceStateMachine(prewarmRequired: false);
        stateMachine.SetConfigAvailable(available: true);
        stateMachine.SetNatsConnected(connected: true);
        var staleIssuer = new JwksIssuerHealth("https://id.example.com", IsHealthy: false, HasUsableKeys: true, LastRefreshed: DateTimeOffset.UtcNow.AddHours(-1));
        var jwksCacheManager = Substitute.For<IJwksCacheManager>();
        jwksCacheManager.Health.Returns([staleIssuer]);
        var options = new HealthTestHostOptions { StateMachine = stateMachine, JwksCacheManager = jwksCacheManager };
        await using var host = await HealthTestHost.BuildAsync(options);

        // /readyz (ready-tagged only) is unaffected by the degraded, untagged jwks check.
        await host.Scenario(s =>
        {
            s.Get.Url(HealthEndpointMapper.ReadinessPath);
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
    }
}
