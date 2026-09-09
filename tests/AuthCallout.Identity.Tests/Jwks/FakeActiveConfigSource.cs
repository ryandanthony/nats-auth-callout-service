// -----------------------------------------------------------------------
// <copyright file="FakeActiveConfigSource.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Identity.Tests.Jwks;

/// <summary>
/// A minimal, hand-rolled <see cref="IActiveConfigSource"/> test double: exposes a mutable
/// <see cref="Current"/> and fires <see cref="Changed"/> on <see cref="SetCurrent"/>. NSubstitute
/// is a poor fit here because tests need to actually raise the event, not just verify a call.
/// </summary>
internal sealed class FakeActiveConfigSource : IActiveConfigSource
{
    private long _version;

    /// <inheritdoc />
    public ActiveConfig? Current { get; private set; }

    /// <inheritdoc />
    public event EventHandler<ActiveConfigChangedEventArgs>? Changed;

    /// <summary>Sets a new current snapshot (auto-incrementing <see cref="ActiveConfig.Version"/>) and raises <see cref="Changed"/>.</summary>
    /// <param name="bootstrap">The bootstrap layer for the new snapshot.</param>
    /// <param name="routing">The routing aggregate for the new snapshot.</param>
    public void SetCurrent(BootstrapConfig bootstrap, RoutingConfig routing)
    {
        _version++;
        var config = new ActiveConfig(bootstrap, routing, _version);
        Current = config;
        Changed?.Invoke(this, new ActiveConfigChangedEventArgs(config));
    }
}
