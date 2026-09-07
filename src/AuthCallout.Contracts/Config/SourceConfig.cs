// -----------------------------------------------------------------------
// <copyright file="SourceConfig.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// The active routing/apps provider selection (ADR 0005) plus watch-health thresholds
/// shared by every source kind (ADR 0017). Exactly one provider kind is active at a time;
/// sources are never combined. Construct via <see cref="Files"/>, <see cref="K8s"/>, or
/// <see cref="Postgres"/> so each kind's settings are enforced at construction.
/// </summary>
/// <param name="Kind">Which provider is active.</param>
/// <param name="FilesSettings">Set only when <see cref="Kind"/> is <see cref="SourceKind.Files"/>.</param>
/// <param name="K8sSettings">Set only when <see cref="Kind"/> is <see cref="SourceKind.K8s"/>.</param>
/// <param name="PostgresSettings">Set only when <see cref="Kind"/> is <see cref="SourceKind.Postgres"/>.</param>
/// <param name="WarnStaleness">Config-source-watch staleness at which the <c>config-source</c> health check degrades and alerts (ADR 0017).</param>
/// <param name="MaxStaleness">Config-source-watch staleness at which the health check becomes unhealthy (<c>/readyz</c> 503), while the service keeps serving last-good (ADR 0017).</param>
/// <param name="ReconnectBackoff">Capped relist/rewatch backoff (ADR 0017).</param>
public sealed record SourceConfig(
    SourceKind Kind,
    FilesSourceSettings? FilesSettings,
    K8sSourceSettings? K8sSettings,
    PostgresSourceSettings? PostgresSettings,
    DurationString WarnStaleness,
    DurationString MaxStaleness,
    DurationString ReconnectBackoff)
{
    /// <summary>Creates a <see cref="SourceKind.Files"/> source config.</summary>
    /// <param name="settings">The files-source settings.</param>
    /// <param name="warnStaleness">The warn-staleness threshold (ADR 0017).</param>
    /// <param name="maxStaleness">The max-staleness threshold (ADR 0017).</param>
    /// <param name="reconnectBackoff">The capped reconnect/rewatch backoff (ADR 0017).</param>
    /// <returns>The constructed files-source config.</returns>
    public static SourceConfig Files(
        FilesSourceSettings settings, DurationString warnStaleness, DurationString maxStaleness, DurationString reconnectBackoff)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SourceConfig(SourceKind.Files, settings, K8sSettings: null, PostgresSettings: null, warnStaleness, maxStaleness, reconnectBackoff);
    }

    /// <summary>Creates a <see cref="SourceKind.K8s"/> source config.</summary>
    /// <param name="settings">The Kubernetes-source settings.</param>
    /// <param name="warnStaleness">The warn-staleness threshold (ADR 0017).</param>
    /// <param name="maxStaleness">The max-staleness threshold (ADR 0017).</param>
    /// <param name="reconnectBackoff">The capped reconnect/rewatch backoff (ADR 0017).</param>
    /// <returns>The constructed Kubernetes-source config.</returns>
#pragma warning disable S100 // "K8s" matches the repo-wide convention (AuthCallout.Config.K8s, SourceKind.K8s); "K8S" would be inconsistent.
    public static SourceConfig K8s(
        K8sSourceSettings settings, DurationString warnStaleness, DurationString maxStaleness, DurationString reconnectBackoff)
#pragma warning restore S100
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SourceConfig(SourceKind.K8s, FilesSettings: null, settings, PostgresSettings: null, warnStaleness, maxStaleness, reconnectBackoff);
    }

    /// <summary>Creates a <see cref="SourceKind.Postgres"/> source config.</summary>
    /// <param name="settings">The Postgres-source settings.</param>
    /// <param name="warnStaleness">The warn-staleness threshold (ADR 0017).</param>
    /// <param name="maxStaleness">The max-staleness threshold (ADR 0017).</param>
    /// <param name="reconnectBackoff">The capped reconnect/rewatch backoff (ADR 0017).</param>
    /// <returns>The constructed Postgres-source config.</returns>
    public static SourceConfig Postgres(
        PostgresSourceSettings settings, DurationString warnStaleness, DurationString maxStaleness, DurationString reconnectBackoff)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SourceConfig(SourceKind.Postgres, FilesSettings: null, K8sSettings: null, settings, warnStaleness, maxStaleness, reconnectBackoff);
    }
}
