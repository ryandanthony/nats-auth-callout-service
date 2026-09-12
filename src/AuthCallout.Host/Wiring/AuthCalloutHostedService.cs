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
/// 05/08. <see cref="StartAsync"/> itself returns almost immediately (it only starts
/// background work): the Generic Host runs every registered <see cref="IHostedService"/>'s
/// <see cref="StartAsync"/> sequentially before it considers itself started, and the health
/// endpoints (D21) are hosted by the same process via the ASP.NET Core web-host service — so
/// blocking this method on config/JWKS/NATS would also block <c>/healthz</c> et al. from ever
/// coming up, defeating their whole purpose (doc 08: "so slow first-config/secret resolution
/// doesn't trip liveness during boot" presumes liveness is already listening). All of the
/// actual doc 05/08 ordering (config-apply loop -&gt; wait for a valid config -&gt; JWKS
/// prewarm -&gt; connect NATS) runs in a background task instead: <see cref="ServiceState.Idle"/>
/// holds no NATS connection at all until a valid config genuinely exists (doc 05, doc 06
/// §Cold start), JWKS prewarm (<see cref="IJwksCacheManager.PrewarmAllAsync"/>, gated by
/// <see cref="AuthCalloutHostedServiceDependencies.PrewarmRequired"/>) happens immediately
/// before connecting, matching <see cref="ServiceStateMachine"/>'s gate order (config, then
/// prewarm, then connection) so <see cref="ServiceState.Serving"/> is only ever reached once
/// all three are genuinely ready. The <c>started</c> startup health check is marked complete
/// once the config-apply loop makes its first decision (doc 08: "first config decision made"
/// — a rejected first batch still counts, not just an accepted one), independently of
/// whether the connect step has happened yet. Graceful shutdown (doc 05 Stopping) tears down
/// in the reverse order: state machine first (stop accepting new work / reflect Stopping in
/// health), then the connect-watcher and NATS connection manager (drains in-flight requests,
/// doc 05), then the config-apply loop and routing provider.
/// </summary>
public sealed class AuthCalloutHostedService : IHostedService, IDisposable
{
    private readonly AuthCalloutHostedServiceDependencies _deps;
    private readonly CancellationTokenSource _runCts = new();
    private Task _backgroundTask = Task.CompletedTask;
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
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Deliberately not awaited: see the type doc comment for why StartAsync must return
        // promptly rather than blocking on config/JWKS/NATS (it would also block Kestrel).
        _backgroundTask = RunBackgroundAsync(_runCts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _deps.Logger.LogInformation("Graceful shutdown starting (doc 05 Stopping).");
        _deps.StateMachine.BeginStopping();

        await _runCts.CancelAsync();

#pragma warning disable VSTHRD003 // _backgroundTask was already started by this instance in StartAsync; awaiting it here is the intended join point, not a foreign/unstarted task.
        try
        {
            await _backgroundTask;
        }
        catch (OperationCanceledException)
        {
            // Expected: RunBackgroundAsync observes _runCts throughout.
        }
#pragma warning restore VSTHRD003

        await _deps.ConnectionManager.DisposeAsync();
        await _deps.RoutingProvider.DisposeAsync();
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

    /// <summary>Runs the whole doc 05/08 startup sequence in the background, after <see cref="StartAsync"/> has already returned.</summary>
    private async Task RunBackgroundAsync(CancellationToken cancellationToken)
    {
        var configApplyTask = RunConfigApplyLoopAsync(cancellationToken);

        try
        {
            await WaitForFirstConfigDecisionAsync(cancellationToken);
            _deps.StartupHealthCheck.MarkStarted();

            await WaitForValidConfigAsync(cancellationToken);

            if (_deps.PrewarmRequired)
            {
                _deps.Logger.LogInformation("Prewarming JWKS for every configured issuer before Serving (doc 08).");
                await _deps.JwksCacheManager.PrewarmAllAsync(cancellationToken);
            }

            _deps.StateMachine.SetPrewarmComplete();

            _deps.Logger.LogInformation("Connecting to NATS as the callout's own service-user connection.");
            await _deps.ConnectionManager.StartAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown, or if a valid config never arrived before the process stopped.
        }

#pragma warning disable VSTHRD003 // configApplyTask was started by this same method, just above; awaiting it here is the intended join point.
        try
        {
            await configApplyTask;
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
#pragma warning restore VSTHRD003
    }

    private async Task RunConfigApplyLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _deps.ConfigApplyLoop.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    /// <summary>
    /// Waits until a valid config is actually available (doc 05: Idle holds no NATS
    /// connection at all until then).
    /// </summary>
    private async Task WaitForValidConfigAsync(CancellationToken cancellationToken)
    {
        if (_deps.ActiveConfigSource.Current is not null)
        {
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, ActiveConfigChangedEventArgs e) => tcs.TrySetResult();
        _deps.ActiveConfigSource.Changed += OnChanged;
        try
        {
            // Re-check after subscribing: a config could have landed between the check above
            // and the subscription taking effect (no lock needed, ActiveConfigSource.Current
            // is a volatile read, doc 08 §Config reload → in-memory swap).
            if (_deps.ActiveConfigSource.Current is not null)
            {
                return;
            }

            await tcs.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _deps.ActiveConfigSource.Changed -= OnChanged;
        }
    }

    /// <summary>
    /// Blocks until the config-apply loop has made its first decision (doc 08 §Health &amp;
    /// readiness "started": "first config decision made") — either a config was accepted or
    /// the first attempt was rejected/absent and the loop has recorded that outcome
    /// (<see cref="ConfigApplyLoop.LastRejectionReason"/> set, or
    /// <see cref="IRoutingConfigProvider.LastSuccessfulSync"/> advanced).
    /// </summary>
    private async Task WaitForFirstConfigDecisionAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_deps.RoutingProvider.LastSuccessfulSync is not null || _deps.ConfigApplyLoop.LastRejectionReason is not null)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }
}
