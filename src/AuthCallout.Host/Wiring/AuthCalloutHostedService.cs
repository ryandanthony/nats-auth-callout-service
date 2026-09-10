// -----------------------------------------------------------------------
// <copyright file="AuthCalloutHostedService.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using AuthCallout.Host.Lifecycle;
using Microsoft.Extensions.Hosting;

namespace AuthCallout.Host.Wiring;

/// <summary>
/// The top-level <see cref="IHostedService"/> orchestrating startup ordering exactly per doc
/// 05/08: bind + validate bootstrap (already done before this runs, doc 08 §Composition
/// "fail-closed" — <c>Program.cs</c> exits before the host even starts on a bad bootstrap
/// file), start the config-apply loop (<see cref="ConfigApplyLoop"/>) so the first routing
/// config can arrive, prewarm JWKS (<see cref="IJwksCacheManager.PrewarmAllAsync"/>, gated by
/// <see cref="AuthCalloutHostedServiceDependencies.PrewarmRequired"/>), then connect NATS and
/// start the responder (<see cref="NatsConnectionManager"/>) — matching
/// <see cref="ServiceStateMachine"/>'s gate order (config, then prewarm, then connection) so
/// <see cref="ServiceState.Serving"/> is only ever reached once all three are genuinely
/// ready. Marks the startup health check complete once this ordering has run once, regardless
/// of whether the first config turned out valid (doc 08: "first config decision made", not
/// "first config accepted" — an Idle service with no config is still a completed decision).
/// Graceful shutdown (doc 05 Stopping) tears down in the reverse order: state machine first
/// (stop accepting new work / reflect Stopping in health), then the NATS connection manager
/// (drains in-flight requests, doc 05), then the config-apply loop and routing provider.
/// </summary>
public sealed class AuthCalloutHostedService : IHostedService, IDisposable
{
    private readonly AuthCalloutHostedServiceDependencies _deps;
    private readonly CancellationTokenSource _runCts = new();
    private Task _configApplyTask = Task.CompletedTask;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="AuthCalloutHostedService"/> class.</summary>
    /// <param name="dependencies">The collaborators this hosted service orchestrates.</param>
    public AuthCalloutHostedService(AuthCalloutHostedServiceDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        dependencies.Validate();
        _deps = dependencies;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _configApplyTask = RunConfigApplyLoopAsync(_runCts.Token);

        await WaitForFirstConfigDecisionAsync(cancellationToken).ConfigureAwait(false);

        if (_deps.PrewarmRequired)
        {
            _deps.Logger.LogInformation("Prewarming JWKS for every configured issuer before Serving (doc 08).");
            await _deps.JwksCacheManager.PrewarmAllAsync(cancellationToken).ConfigureAwait(false);
        }

        _deps.StateMachine.SetPrewarmComplete();
        _deps.StartupHealthCheck.MarkStarted();

        _deps.Logger.LogInformation("Connecting to NATS as the callout's own service-user connection.");
        await _deps.ConnectionManager.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _deps.Logger.LogInformation("Graceful shutdown starting (doc 05 Stopping).");
        _deps.StateMachine.BeginStopping();

        await _deps.ConnectionManager.DisposeAsync().ConfigureAwait(false);
        await _runCts.CancelAsync().ConfigureAwait(false);

        try
        {
            await _configApplyTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: RunConfigApplyLoopAsync observes _runCts.
        }

        await _deps.RoutingProvider.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _runCts.Dispose();
        _disposed = true;
    }

    private async Task RunConfigApplyLoopAsync(CancellationToken cancellationToken)
    {
        // Started fresh in this async method's own context (not via a bare Task.Run capturing
        // an outer scope), so awaiting it later in StopAsync is the intended join point rather
        // than a foreign/unstarted-task deadlock risk.
        await Task.Yield();
        try
        {
            await _deps.ConfigApplyLoop.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    /// <summary>
    /// Blocks until the config-apply loop has made its first decision (doc 08 §Health &amp;
    /// readiness "started": "first config decision made") — either a config was accepted
    /// (<see cref="ServiceState.Serving"/>-eligible once the other gates clear too) or the
    /// first attempt was rejected/absent and the loop has recorded that outcome
    /// (<see cref="ConfigApplyLoop.LastRejectionReason"/> set, or
    /// <see cref="IRoutingConfigProvider.LastSuccessfulSync"/> advanced). Bounded only by
    /// <paramref name="cancellationToken"/> (the orchestrator's own startup timeout / startup
    /// probe budget governs how long this may take, doc 08: "so slow first-config/secret
    /// resolution doesn't trip liveness during boot").
    /// </summary>
    private async Task WaitForFirstConfigDecisionAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_deps.RoutingProvider.LastSuccessfulSync is not null || _deps.ConfigApplyLoop.LastRejectionReason is not null)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
    }
}
