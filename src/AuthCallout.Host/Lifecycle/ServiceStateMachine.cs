// -----------------------------------------------------------------------
// <copyright file="ServiceStateMachine.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;

namespace AuthCallout.Host.Lifecycle;

/// <summary>
/// The <see cref="IServiceStateProvider"/> implementation and the single source of truth for
/// the doc 05 state diagram: <c>Starting -&gt; Idle/Serving -&gt; Stopping</c>. Recomputes
/// <see cref="Current"/> from three independently-set gates (valid config present, JWKS
/// prewarm complete, NATS connected and subscribed) every time one of them changes
/// (doc 08 §Process composition: "Serving entry requires valid config + secrets resolved +
/// JWKS prewarm complete + NATS connected + subscribed"). <see cref="ServiceState.Idle"/> is
/// simply "any gate not yet satisfied" — in particular it is the state whenever
/// <see cref="SetNatsConnected"/> reports false, so a lost connection (ADR 0017 §1) collapses
/// straight back to Idle exactly like a lost config does (ADR 0006), with no separate
/// "disconnected but otherwise fine" state. <see cref="BeginStopping"/> is terminal: once
/// called, no further gate change reopens <see cref="ServiceState.Serving"/> (doc 05:
/// <c>Stopping -&gt; [*]</c>, no state re-enters after Stopping).
/// </summary>
public sealed class ServiceStateMachine : IServiceStateProvider
{
    private readonly Lock _gate = new();
    private readonly bool _prewarmRequired;

    private ServiceState _current = ServiceState.Starting;
    private ConfigFreshness _freshness = ConfigFreshness.Fresh;
    private bool _hasValidConfig;
    private bool _prewarmComplete;
    private bool _natsConnected;
    private bool _stopping;

    /// <summary>Initializes a new instance of the <see cref="ServiceStateMachine"/> class.</summary>
    /// <param name="prewarmRequired">
    /// Bootstrap <c>jwks.prewarmRequired</c> (doc 08 §JWKS cache manager). When false the
    /// prewarm gate is satisfied from construction (best-effort prewarm never blocks
    /// <see cref="ServiceState.Serving"/>).
    /// </param>
    public ServiceStateMachine(bool prewarmRequired)
    {
        _prewarmRequired = prewarmRequired;
        _prewarmComplete = !prewarmRequired;
    }

    /// <summary>Raised after every state transition (old state != new state), including into <see cref="ServiceState.Stopping"/>.</summary>
    public event EventHandler<ServiceStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public ServiceState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public ConfigFreshness Freshness
    {
        get
        {
            lock (_gate)
            {
                return _current == ServiceState.Serving ? _freshness : ConfigFreshness.Fresh;
            }
        }
    }

    /// <summary>
    /// Reports whether a valid, applied <see cref="AuthCallout.Contracts.Config.ActiveConfig"/>
    /// is currently available (doc 06 §Config load and reload: "reject the whole batch, keep
    /// last-good" never clears this once true; only "no last-good" — an invalid first config,
    /// or the source going empty with nothing previously accepted — clears it).
    /// </summary>
    /// <param name="available">True once at least one valid config has been applied and not since lost.</param>
    public void SetConfigAvailable(bool available) => Recompute(() => _hasValidConfig = available);

    /// <summary>Reports JWKS prewarm completion (doc 08: gates <see cref="ServiceState.Serving"/> when prewarm is required).</summary>
    public void SetPrewarmComplete() => Recompute(() => _prewarmComplete = true);

    /// <summary>
    /// Reports the callout's own NATS connection state (ADR 0017 §1: connected and
    /// subscribed drives <see cref="ServiceState.Serving"/>; a drop drives straight back to
    /// <see cref="ServiceState.Idle"/>, auto-recovering on reconnect).
    /// </summary>
    /// <param name="connected">True once connected and subscribed to <c>$SYS.REQ.USER.AUTH</c>.</param>
    public void SetNatsConnected(bool connected) => Recompute(() => _natsConnected = connected);

    /// <summary>
    /// Records the config-source freshness substate (ADR 0017 staleness table), read back via
    /// <see cref="Freshness"/> while <see cref="Current"/> is <see cref="ServiceState.Serving"/>.
    /// </summary>
    /// <param name="freshness">The newly computed freshness substate.</param>
    public void SetFreshness(ConfigFreshness freshness)
    {
        lock (_gate)
        {
            _freshness = freshness;
        }
    }

    /// <summary>
    /// Begins graceful shutdown (doc 05: SIGINT/SIGTERM from any prior state). Terminal: no
    /// further <c>Set*</c> call reopens <see cref="ServiceState.Serving"/> afterward.
    /// </summary>
    public void BeginStopping()
    {
        ServiceState previous;
        lock (_gate)
        {
            previous = _current;
            _stopping = true;
            _current = ServiceState.Stopping;
        }

        RaiseIfChanged(previous, ServiceState.Stopping);
    }

    /// <summary>Gets a value indicating whether prewarm is required before <see cref="ServiceState.Serving"/> (test/diagnostic visibility).</summary>
    /// <value>True when bootstrap <c>jwks.prewarmRequired</c> is true.</value>
    public bool PrewarmRequired => _prewarmRequired;

    private void Recompute(Action mutate)
    {
        ServiceState previous;
        ServiceState next;
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            previous = _current;
            mutate();
            next = _hasValidConfig && _prewarmComplete && _natsConnected ? ServiceState.Serving : ServiceState.Idle;
            _current = next;
        }

        RaiseIfChanged(previous, next);
    }

    private void RaiseIfChanged(ServiceState previous, ServiceState next)
    {
        if (previous != next)
        {
            StateChanged?.Invoke(this, new ServiceStateChangedEventArgs(previous, next));
        }
    }
}
