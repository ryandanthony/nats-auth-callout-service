// -----------------------------------------------------------------------
// <copyright file="ServiceStateMachineTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using AuthCallout.Host.Lifecycle;

namespace AuthCallout.Host.Tests.Lifecycle;

/// <summary>
/// Doc 05 state-diagram unit tests for <see cref="ServiceStateMachine"/>: Starting -&gt;
/// Idle/Serving -&gt; Stopping, gated on config + prewarm + connection (doc 08 §Process
/// composition), and ADR 0017 §1's "lost connection collapses straight back to Idle".
/// </summary>
public sealed class ServiceStateMachineTests
{
    [Fact]
    public void Starts_in_Starting()
    {
        var sut = new ServiceStateMachine(prewarmRequired: true);
        Assert.Equal(ServiceState.Starting, sut.Current);
    }

    [Fact]
    public void Stays_Idle_until_all_three_gates_clear()
    {
        var sut = new ServiceStateMachine(prewarmRequired: true);

        sut.SetConfigAvailable(available: true);
        Assert.Equal(ServiceState.Idle, sut.Current);

        sut.SetPrewarmComplete();
        Assert.Equal(ServiceState.Idle, sut.Current);

        sut.SetNatsConnected(connected: true);
        Assert.Equal(ServiceState.Serving, sut.Current);
    }

    [Fact]
    public void Gate_order_does_not_matter_only_all_three_being_true_does()
    {
        var sut = new ServiceStateMachine(prewarmRequired: true);

        sut.SetNatsConnected(connected: true);
        sut.SetPrewarmComplete();
        Assert.Equal(ServiceState.Idle, sut.Current);

        sut.SetConfigAvailable(available: true);
        Assert.Equal(ServiceState.Serving, sut.Current);
    }

    [Fact]
    public void PrewarmRequired_false_satisfies_the_prewarm_gate_from_construction()
    {
        var sut = new ServiceStateMachine(prewarmRequired: false);

        sut.SetConfigAvailable(available: true);
        sut.SetNatsConnected(connected: true);

        Assert.Equal(ServiceState.Serving, sut.Current);
    }

    [Fact]
    public void Lost_NATS_connection_collapses_Serving_back_to_Idle_ADR_0017()
    {
        var sut = new ServiceStateMachine(prewarmRequired: false);
        sut.SetConfigAvailable(available: true);
        sut.SetNatsConnected(connected: true);
        Assert.Equal(ServiceState.Serving, sut.Current);

        sut.SetNatsConnected(connected: false);

        Assert.Equal(ServiceState.Idle, sut.Current);
    }

    [Fact]
    public void Reconnect_after_a_drop_returns_to_Serving()
    {
        var sut = new ServiceStateMachine(prewarmRequired: false);
        sut.SetConfigAvailable(available: true);
        sut.SetNatsConnected(connected: true);
        sut.SetNatsConnected(connected: false);
        Assert.Equal(ServiceState.Idle, sut.Current);

        sut.SetNatsConnected(connected: true);

        Assert.Equal(ServiceState.Serving, sut.Current);
    }

    [Fact]
    public void Bad_reload_with_no_last_good_keeps_Idle_ADR_0006()
    {
        var sut = new ServiceStateMachine(prewarmRequired: false);
        sut.SetNatsConnected(connected: true);

        // A rejected first batch never calls SetConfigAvailable(true); it stays false.
        sut.SetConfigAvailable(available: false);

        Assert.Equal(ServiceState.Idle, sut.Current);
    }

    [Fact]
    public void Config_lost_after_Serving_returns_to_Idle()
    {
        var sut = new ServiceStateMachine(prewarmRequired: false);
        sut.SetConfigAvailable(available: true);
        sut.SetNatsConnected(connected: true);
        Assert.Equal(ServiceState.Serving, sut.Current);

        sut.SetConfigAvailable(available: false);

        Assert.Equal(ServiceState.Idle, sut.Current);
    }

    [Fact]
    public void BeginStopping_transitions_to_Stopping_from_any_prior_state()
    {
        var sut = new ServiceStateMachine(prewarmRequired: false);
        sut.SetConfigAvailable(available: true);
        sut.SetNatsConnected(connected: true);

        sut.BeginStopping();

        Assert.Equal(ServiceState.Stopping, sut.Current);
    }

    [Fact]
    public void BeginStopping_is_terminal_no_later_gate_change_reopens_Serving()
    {
        var sut = new ServiceStateMachine(prewarmRequired: false);
        sut.BeginStopping();

        sut.SetConfigAvailable(available: true);
        sut.SetNatsConnected(connected: true);
        sut.SetPrewarmComplete();

        Assert.Equal(ServiceState.Stopping, sut.Current);
    }

    [Fact]
    public void StateChanged_fires_once_per_actual_transition_not_per_call()
    {
        var sut = new ServiceStateMachine(prewarmRequired: false);
        var transitions = new List<(ServiceState previous, ServiceState next)>();
        sut.StateChanged += (_, e) => transitions.Add((e.Previous, e.Next));

        sut.SetConfigAvailable(available: true); // still Idle: no transition (Starting -> Idle IS a transition though)
        sut.SetNatsConnected(connected: true); // Idle -> Serving

        Assert.Equal((ServiceState.Starting, ServiceState.Idle), transitions[0]);
        Assert.Equal((ServiceState.Idle, ServiceState.Serving), transitions[1]);
        Assert.Equal(2, transitions.Count);
    }

    [Fact]
    public void Freshness_defaults_to_Fresh_and_is_harmless_when_not_Serving()
    {
        var sut = new ServiceStateMachine(prewarmRequired: true);
        Assert.Equal(ConfigFreshness.Fresh, sut.Freshness);

        sut.SetFreshness(ConfigFreshness.StaleUnready);

        // Not Serving yet, so Freshness reports the harmless default regardless of what was set.
        Assert.Equal(ConfigFreshness.Fresh, sut.Freshness);
    }

    [Fact]
    public void Freshness_reflects_set_value_while_Serving()
    {
        var sut = new ServiceStateMachine(prewarmRequired: false);
        sut.SetConfigAvailable(available: true);
        sut.SetNatsConnected(connected: true);
        Assert.Equal(ServiceState.Serving, sut.Current);

        sut.SetFreshness(ConfigFreshness.StaleDegraded);

        Assert.Equal(ConfigFreshness.StaleDegraded, sut.Freshness);
    }
}
