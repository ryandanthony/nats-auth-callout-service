// -----------------------------------------------------------------------
// <copyright file="ApplyLoopHarness.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Host.Lifecycle;

namespace AuthCallout.Host.Tests.Lifecycle;

/// <summary>
/// Wires a <see cref="ConfigApplyLoop"/> with its lifecycle collaborators and runs it in the
/// background, so <see cref="ConfigApplyLoopTests"/> can express each ADR→test map row as a
/// short, intention-revealing test body instead of repeating this wiring per test (same
/// pattern as <c>AuthCallout.Nats.Tests.Callout.ResponderHarness</c>).
/// </summary>
internal sealed class ApplyLoopHarness : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _runTask;

    private ApplyLoopHarness(ConfigApplyLoop sut, ActiveConfigSource activeConfigSource, ServiceStateMachine stateMachine)
    {
        Sut = sut;
        ActiveConfigSource = activeConfigSource;
        StateMachine = stateMachine;
        _runTask = sut.RunAsync(_cts.Token);
    }

    public ConfigApplyLoop Sut { get; }

    public ActiveConfigSource ActiveConfigSource { get; }

    public ServiceStateMachine StateMachine { get; }

    /// <summary>Creates a harness in the Idle state (no NATS connection reported), for reload/reject tests that don't care about <see cref="ServiceState"/>.</summary>
    public static ApplyLoopHarness Create(
        FakeRoutingConfigProvider provider, Func<ActiveConfig, CancellationToken, Task>? onApplied = null)
    {
        var activeConfigSource = new ActiveConfigSource();
        var stateMachine = new ServiceStateMachine(prewarmRequired: false);
        var bootstrap = HostTestBuilders.MinimalBootstrap();
        var options = new ConfigApplyLoopOptions(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), OnApplied: onApplied);
        var sut = new ConfigApplyLoop(provider, bootstrap, activeConfigSource, stateMachine, options);
        return new ApplyLoopHarness(sut, activeConfigSource, stateMachine);
    }

    /// <summary>Creates a harness with the connection gate already satisfied, so an accepted config reaches <see cref="ServiceState.Serving"/> (for staleness/freshness tests).</summary>
    public static ApplyLoopHarness CreateServing(FakeRoutingConfigProvider provider, TimeSpan warnStaleness, TimeSpan maxStaleness)
    {
        var activeConfigSource = new ActiveConfigSource();
        var stateMachine = new ServiceStateMachine(prewarmRequired: false);
        stateMachine.SetNatsConnected(connected: true);
        var bootstrap = HostTestBuilders.MinimalBootstrap();
        var options = new ConfigApplyLoopOptions(warnStaleness, maxStaleness);
        var sut = new ConfigApplyLoop(provider, bootstrap, activeConfigSource, stateMachine, options);
        return new ApplyLoopHarness(sut, activeConfigSource, stateMachine);
    }

    public async Task StopAsync()
    {
        await _cts.CancelAsync();
        try
        {
#pragma warning disable VSTHRD003 // _runTask was already started by this instance's constructor; awaiting it here is the intended join point, not a foreign/unstarted task.
            await _runTask;
#pragma warning restore VSTHRD003
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }
    }

    public void Dispose() => _cts.Dispose();
}
