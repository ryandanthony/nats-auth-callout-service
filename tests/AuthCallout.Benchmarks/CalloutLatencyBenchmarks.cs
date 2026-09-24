// -----------------------------------------------------------------------
// <copyright file="CalloutLatencyBenchmarks.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Integration.Fixtures;
using AuthCallout.Integration.Support;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using NATS.Client.Core;

namespace AuthCallout.Benchmarks;

/// <summary>
/// Benchmarks the real, end-to-end auth-callout request path against real infrastructure: a
/// <c>nats:2.11</c> container with <c>auth_callout</c> configured, a real Keycloak container,
/// and the production <c>CalloutResponder</c>/component graph (<see cref="HostFixture"/>, the
/// same fixture <c>tests/AuthCallout.Integration</c> proves against). Each iteration is one
/// full <see cref="NatsConnection.ConnectAsync"/> — the same trigger LoadService and doc 08 use:
/// a new connection drives exactly one <c>$SYS.REQ.USER.AUTH</c> round trip through the callout.
/// </summary>
/// <remarks>
/// Configured for <see cref="InProcessEmitToolchain"/> explicitly (BenchmarkDotNet's default
/// spawns a separate generated process per run, which would each pay to restart the
/// NATS/Keycloak containers, and would make <see cref="ServerSideHistogram"/> unable to observe
/// the callout's meter at all — it can only listen inside the same process that runs the real
/// <c>CalloutResponder</c>). <see cref="RunStrategy.Monitoring"/>: this is a macrobenchmark over
/// real I/O, not a tight CPU microbenchmark, so BenchmarkDotNet's usual pilot/overhead-estimation
/// phases are skipped in favor of a fixed warmup then fixed measured iteration count.
/// </remarks>
public class CalloutLatencyBenchmarks
{
    /// <summary>How many unmeasured connects run before the measured iterations (JIT warmup, first-connect one-time costs).</summary>
    private const int WarmupIterations = 20;

    /// <summary>How many measured connects make up the reported distribution. Large enough for a stable p99 (D17: percentile of a percentile needs real sample depth).</summary>
    private const int MeasuredIterations = 200;

    private NatsFixture? _nats;
    private KeycloakFixture? _keycloak;
    private HostFixture? _host;
    private string? _token;

    /// <summary>Gets the job configuration: in-process, one launch, fixed warmup/iteration counts, one invocation per iteration (each invocation is a full NATS connect, not a sub-millisecond operation to unroll).</summary>
    /// <returns>The configured job.</returns>
    public static Job BuildJob() => Job.Default
        .WithToolchain(InProcessEmitToolchain.Instance)
        .WithStrategy(RunStrategy.Monitoring)
        .WithLaunchCount(1)
        .WithWarmupCount(WarmupIterations)
        .WithIterationCount(MeasuredIterations)
        .WithInvocationCount(1)
        .WithUnrollFactor(1);

    /// <summary>Starts the real NATS + Keycloak containers and the real callout, then mints one real token to reuse across iterations (mirrors LoadService: one token per worker, reused across connects).</summary>
#pragma warning disable VSTHRD002 // BenchmarkDotNet's [GlobalSetup]/[GlobalCleanup] contract is a synchronous MethodInfo it invokes by reflection outside any iteration timing (Descriptor.GlobalSetupMethod), with no async overload; blocking here is the documented pattern for async setup/teardown work (BenchmarkDotNet's own samples use the identical GetAwaiter().GetResult() shape), never inside a measured iteration.
    [GlobalSetup]
    public void GlobalSetup() => GlobalSetupAsync().GetAwaiter().GetResult();

    /// <summary>Tears down the callout and the containers.</summary>
    [GlobalCleanup]
    public void GlobalCleanup() => GlobalCleanupAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002

    /// <summary>One full NATS connect through the real auth callout: TCP + NATS handshake, the server's <c>$SYS.REQ.USER.AUTH</c> round trip, the callout's real resolution/KV-write/mint/reply, the server applying the reply.</summary>
    /// <returns>A task that completes once the connection is fully authenticated and open.</returns>
    [Benchmark(Description = "Real auth-callout connect (NATS + Keycloak, production ComponentGraph)")]
    public async Task ConnectThroughRealCalloutAsync()
    {
        await using var connection = new NatsConnection(new NatsOpts
        {
            Url = _nats!.ConnectionUrl,
            AuthOpts = new NatsAuthOpts { Token = _token! },
            InboxPrefix = AppSlug.From(TestApplications.NotesAppName).ClientInboxPrefix(JwtInspector.ReadClaim(_token!, "sub")),
        });
        await connection.ConnectAsync();
    }

    private async Task GlobalSetupAsync()
    {
        _nats = new NatsFixture();
        _keycloak = new KeycloakFixture();
        await Task.WhenAll(_nats.InitializeAsync(), _keycloak.InitializeAsync());

        _host = await HostFixture.StartAsync(
            _nats,
            _keycloak.CertificatePem,
            [TestApplications.NotesApp(_keycloak.IssuerAuthority)],
            anonymous: null,
            CancellationToken.None);

        _token = await _keycloak.GetPasswordGrantTokenAsync(
            IntegrationRealm.NotesClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);
    }

    private async Task GlobalCleanupAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        if (_nats is not null)
        {
            await _nats.DisposeAsync();
        }

        if (_keycloak is not null)
        {
            await _keycloak.DisposeAsync();
        }
    }
}
