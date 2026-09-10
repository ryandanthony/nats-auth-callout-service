// -----------------------------------------------------------------------
// <copyright file="ActiveConfigSource.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Host.Lifecycle;

/// <summary>
/// The <see cref="IActiveConfigSource"/> implementation: the single mutable home of the
/// <see cref="ActiveConfig"/> snapshot behind a <see langword="volatile"/> reference (doc 08
/// §Config reload → in-memory swap: "a single immutable snapshot behind a volatile
/// reference... In-flight requests keep their snapshot; new requests read the new one. No
/// lock on the hot path."). Only the config-apply loop (<see cref="ConfigApplyLoop"/>) ever
/// calls <see cref="Apply"/>; every other consumer (P05/P06/P08, and this Host's own health
/// checks) only ever reads <see cref="Current"/> or subscribes to <see cref="Changed"/>.
/// </summary>
public sealed class ActiveConfigSource : IActiveConfigSource
{
    private ActiveConfig? _current;

    /// <inheritdoc />
    public ActiveConfig? Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public event EventHandler<ActiveConfigChangedEventArgs>? Changed;

    /// <summary>
    /// Swaps in a newly-accepted snapshot (doc 08: "one reference swap") and raises
    /// <see cref="Changed"/>. Never called for a rejected reload (ADR 0006: reject keeps
    /// <see cref="Current"/> unchanged, no notification) — that decision is the caller's
    /// (<see cref="ConfigApplyLoop"/>), not this type's.
    /// </summary>
    /// <param name="config">The new snapshot to publish as <see cref="Current"/>.</param>
    public void Apply(ActiveConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Volatile.Write(ref _current, config);
        Changed?.Invoke(this, new ActiveConfigChangedEventArgs(config));
    }
}
