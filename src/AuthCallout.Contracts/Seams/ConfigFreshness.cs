// -----------------------------------------------------------------------
// <copyright file="ConfigFreshness.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// The config-source freshness substate of <see cref="ServiceState.Serving"/> (ADR 0017's
/// staleness table). Only meaningful while <see cref="ServiceState.Serving"/>; a
/// <see cref="ServiceState.Idle"/> or <see cref="ServiceState.Starting"/> service has no
/// freshness to report.
/// </summary>
public enum ConfigFreshness
{
    /// <summary>Staleness below <c>warnStaleness</c>. <c>config-source</c> health is Healthy, <c>/readyz</c> 200.</summary>
    Fresh,

    /// <summary>Staleness between <c>warnStaleness</c> and <c>maxStaleness</c>. <c>config-source</c> health is Degraded (alerted), <c>/readyz</c> still 200.</summary>
    StaleDegraded,

    /// <summary>Staleness at or above <c>maxStaleness</c>. <c>config-source</c> health is Unhealthy, <c>/readyz</c> 503 — while still serving last-good.</summary>
    StaleUnready,
}
