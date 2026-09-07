// -----------------------------------------------------------------------
// <copyright file="IActiveConfigSource.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// The current <see cref="ActiveConfig"/> snapshot and change notifications (doc 08 §Config
/// reload → in-memory swap: "Active config is a single immutable snapshot behind a
/// <see langword="volatile"/> reference... In-flight requests keep their snapshot; new requests read
/// the new one"). Implemented by <c>AuthCallout.Host</c>'s config-apply loop (P13);
/// consumed by the resolution pipeline (P05), the JWKS cache manager's issuer tracking
/// (P06), and the identity store writer's bucket provisioning (P08).
/// </summary>
public interface IActiveConfigSource
{
    /// <summary>
    /// Gets the current snapshot. Null until the very first valid config is applied (doc 05:
    /// the service is <c>Idle</c> with no config until then). A caller reads this once per
    /// unit of work (e.g. once per auth request) so an in-flight resolution keeps a
    /// consistent view even if a reload swaps in a new snapshot concurrently.
    /// </summary>
    /// <value>The current snapshot, or null before the first valid config is applied.</value>
    ActiveConfig? Current { get; }

    /// <summary>
    /// Fires after every accepted config-apply (a new, higher <see cref="ActiveConfig.Version"/>
    /// became <see cref="Current"/>). Does not fire for a rejected reload (ADR 0006: reject
    /// keeps last-good, <see cref="Current"/> is unchanged, no notification). Subscribers
    /// (e.g. P06's per-issuer JWKS tracking) diff the new snapshot against what they already
    /// know rather than assuming every notification is a full-aggregate change.
    /// </summary>
    event EventHandler<ActiveConfigChangedEventArgs>? Changed;
}
