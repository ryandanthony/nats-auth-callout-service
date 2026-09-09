// -----------------------------------------------------------------------
// <copyright file="NotificationSignal.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Config.Postgres.Repository;

/// <summary>
/// A content-free "something changed" signal from <see cref="IAuthApplicationRepository.ListenForChangesAsync"/>.
/// The provider always re-lists on receipt rather than trusting any payload, so the signal
/// carries no data — this mirrors the migration's <c>PERFORM pg_notify(..., '')</c>, which sends
/// an empty payload deliberately (doc09 §Rationale: NOTIFY is a latency optimization, never the
/// source of truth for what changed).
/// </summary>
public sealed record NotificationSignal
{
    /// <summary>Gets the shared instance (this type carries no data, so one instance suffices).</summary>
    /// <value>The shared notification signal instance.</value>
    public static NotificationSignal Instance { get; } = new();
}
