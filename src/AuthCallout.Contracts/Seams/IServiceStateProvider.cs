// -----------------------------------------------------------------------
// <copyright file="IServiceStateProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// Exposes the service's current lifecycle state and config-freshness substate (doc 05,
/// ADR 0017) for the health-check surfaces (doc 08 §Health &amp; readiness:
/// <c>config-active</c> reads <see cref="Current"/>, <c>config-source</c> reads
/// <see cref="Freshness"/>). Implemented and owned by <c>AuthCallout.Host</c>'s state
/// machine (P13); read by the health check implementations (also P13, but modeled as a
/// Contracts seam so a health check could be unit-tested against a substitute).
/// </summary>
public interface IServiceStateProvider
{
    /// <summary>Gets the current top-level lifecycle state.</summary>
    /// <value>The current lifecycle state.</value>
    ServiceState Current { get; }

    /// <summary>
    /// Gets the config freshness substate. Only meaningful while <see cref="Current"/> is
    /// <see cref="ServiceState.Serving"/>; implementations return
    /// <see cref="ConfigFreshness.Fresh"/> as the harmless default otherwise (there is
    /// nothing stale to report when not serving).
    /// </summary>
    /// <value>The current config freshness substate.</value>
    ConfigFreshness Freshness { get; }
}
