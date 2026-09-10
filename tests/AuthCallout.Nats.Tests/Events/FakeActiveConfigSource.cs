// -----------------------------------------------------------------------
// <copyright file="FakeActiveConfigSource.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Nats.Tests.Events;

/// <summary>
/// A minimal, hand-rolled <see cref="IActiveConfigSource"/> test double: exposes a mutable
/// <see cref="Current"/>. <see cref="EventEmitter"/> only ever reads <see cref="Current"/>
/// once per <see cref="EventEmitter.EmitAsync"/> call, so <see cref="Changed"/> is never
/// raised by this fixture (same reasoning as P06's identically-shaped fixture: NSubstitute is
/// a poor fit when a test needs a concrete settable property, not call verification).
/// </summary>
internal sealed class FakeActiveConfigSource : IActiveConfigSource
{
    /// <inheritdoc />
    public ActiveConfig? Current { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// Never raised by this fixture: <see cref="EventEmitter"/> only ever reads
    /// <see cref="Current"/>, so no test needs this event to fire. The accessors are
    /// intentionally no-ops (Sonar S108 suppressed rather than worked around, since a
    /// deliberately empty implementation is the accurate documentation of that fact).
    /// </remarks>
#pragma warning disable S108
    public event EventHandler<ActiveConfigChangedEventArgs>? Changed
    {
        add
        {
            // Intentionally unobserved by this fixture; see remarks.
        }

        remove
        {
            // Intentionally unobserved by this fixture; see remarks.
        }
    }
#pragma warning restore S108
}
