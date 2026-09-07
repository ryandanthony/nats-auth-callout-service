// -----------------------------------------------------------------------
// <copyright file="DenyReasonMap.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Decisions;

/// <summary>
/// The total, exhaustive mapping from every internal <see cref="DenyReason"/> to its wire
/// <see cref="EventReason"/> (QUESTIONS C12: two enums + mapping). "Total" means every
/// current <see cref="DenyReason"/> member has an entry here — enforced by an exhaustiveness
/// unit test that enumerates <see cref="DenyReason"/> via reflection, so adding a new
/// <see cref="DenyReason"/> without extending this map fails CI rather than silently
/// falling through at runtime.
/// </summary>
public static class DenyReasonMap
{
    private static readonly IReadOnlyDictionary<DenyReason, EventReason> Map = new Dictionary<DenyReason, EventReason>
    {
        [DenyReason.NoToken] = EventReason.InvalidToken,
        [DenyReason.UnknownIssuer] = EventReason.NoIssuerMatch,
        [DenyReason.SignatureInvalid] = EventReason.InvalidToken,
        [DenyReason.Expired] = EventReason.InvalidToken,
        [DenyReason.AudienceMismatch] = EventReason.InvalidToken,
        [DenyReason.AlgorithmRejected] = EventReason.InvalidToken,
        [DenyReason.NoAppMatch] = EventReason.NoAppMatch,
        [DenyReason.AmbiguousApp] = EventReason.AmbiguousApp,
        [DenyReason.NoProfileMatch] = EventReason.NoProfileMatch,
        [DenyReason.RequiredVariableMissing] = EventReason.GrantConstructionFailed,
        [DenyReason.UnsafeTemplateValue] = EventReason.GrantConstructionFailed,
        [DenyReason.NameClaimError] = EventReason.GrantConstructionFailed,
        [DenyReason.AnonymousNoContributions] = EventReason.InvalidToken,
    };

    /// <summary>
    /// Gets the full, immutable mapping table, exposed for the exhaustiveness test (which
    /// diffs its key set against every defined <see cref="DenyReason"/>) and for any
    /// consumer that needs to enumerate it rather than call <see cref="ToEventReason"/>
    /// one value at a time.
    /// </summary>
    /// <value>The full mapping table.</value>
    public static IReadOnlyDictionary<DenyReason, EventReason> All => Map;

    /// <summary>Maps one internal <see cref="DenyReason"/> to its wire <see cref="EventReason"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="reason"/> has no entry in the map. This indicates a new
    /// <see cref="DenyReason"/> was added without updating this class — the exhaustiveness
    /// test is meant to catch this before it ships, so hitting this at runtime signals the
    /// test was bypassed or is itself out of date.
    /// </exception>
    /// <param name="reason">The internal deny reason to map.</param>
    /// <returns>The corresponding wire event reason.</returns>
    public static EventReason ToEventReason(this DenyReason reason) =>
        Map.TryGetValue(reason, out var eventReason)
            ? eventReason
            : throw new ArgumentOutOfRangeException(nameof(reason), reason, "No DenyReasonMap entry for this DenyReason; the map is not exhaustive.");
}
