// -----------------------------------------------------------------------
// <copyright file="PostgresSourceSettings.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// Settings for the PostgreSQL source (QUESTIONS A2, P12). The connection string is a
/// <see cref="SecretRef"/> (never plain config); change detection combines a poll loop
/// (which doubles as the ADR 0017 <c>LastSuccessfulSync</c> heartbeat) with a LISTEN/NOTIFY
/// channel as a latency optimization — the poll loop always runs regardless, so NOTIFY is
/// never load-bearing for correctness.
/// </summary>
/// <param name="ConnectionString">The PostgreSQL connection string, as a secret reference.</param>
/// <param name="PollInterval">How often the provider polls for changes; also the staleness heartbeat cadence.</param>
/// <param name="NotifyChannel">The LISTEN/NOTIFY channel name the provider subscribes to. Defaults to <see cref="DefaultNotifyChannel"/>, matching P12's migration-created trigger.</param>
public sealed record PostgresSourceSettings(SecretRef ConnectionString, DurationString PollInterval, string NotifyChannel = PostgresSourceSettings.DefaultNotifyChannel)
{
#pragma warning disable S2339 // Must stay a compile-time constant: used as this record's default parameter value, which C# requires to be a const.
    /// <summary>The default notify channel name, matching P12's migration-created trigger.</summary>
    public const string DefaultNotifyChannel = "auth_applications_changed";
#pragma warning restore S2339
}
