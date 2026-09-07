// -----------------------------------------------------------------------
// <copyright file="EventReasonWireFormat.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Decisions;

/// <summary>
/// Converts <see cref="EventReason"/> to and from the exact lower-<c>snake_case</c> wire
/// strings in <c>docs/design/09-schemas-and-versioning.md</c> §1's field table, so every
/// producer (P09's <c>IEventEmitter</c>) and consumer (schema fixtures, P14) uses the
/// identical literal set without re-typing it.
/// </summary>
public static class EventReasonWireFormat
{
    /// <summary>Returns the doc-09 wire string for a reason code (e.g. <see cref="EventReason.NoIssuerMatch"/> → <c>"no_issuer_match"</c>).</summary>
    /// <param name="reason">The reason code to format.</param>
    /// <returns>The doc-09 lower-<c>snake_case</c> wire string.</returns>
    public static string ToWireString(this EventReason reason) => reason switch
    {
        EventReason.Anonymous => "anonymous",
        EventReason.InvalidToken => "invalid_token",
        EventReason.NoIssuerMatch => "no_issuer_match",
        EventReason.NoAppMatch => "no_app_match",
        EventReason.AmbiguousApp => "ambiguous_app",
        EventReason.NoProfileMatch => "no_profile_match",
        EventReason.GrantConstructionFailed => "grant_construction_failed",
        EventReason.Granted => "granted",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unrecognized EventReason."),
    };

    /// <summary>Parses a doc-09 wire string back into an <see cref="EventReason"/>.</summary>
    /// <param name="wireValue">The doc-09 wire string to parse.</param>
    /// <returns>The parsed reason code.</returns>
    /// <exception cref="FormatException">The string is not one of doc 09's known reason codes.</exception>
    public static EventReason ParseWireString(string wireValue) => wireValue switch
    {
        "anonymous" => EventReason.Anonymous,
        "invalid_token" => EventReason.InvalidToken,
        "no_issuer_match" => EventReason.NoIssuerMatch,
        "no_app_match" => EventReason.NoAppMatch,
        "ambiguous_app" => EventReason.AmbiguousApp,
        "no_profile_match" => EventReason.NoProfileMatch,
        "grant_construction_failed" => EventReason.GrantConstructionFailed,
        "granted" => EventReason.Granted,
        _ => throw new FormatException($"'{wireValue}' is not a known doc-09 event reason code."),
    };
}
