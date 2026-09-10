// -----------------------------------------------------------------------
// <copyright file="AuthOutcomeEventPayload.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Nats.Events;

/// <summary>
/// The wire shape of the v1 auth outcome event (ADR 0008, doc 09 §1,
/// <c>schemas/events/auth-outcome/v1.json</c>). Field names and casing are dictated by the
/// schema: <see cref="System.Text.Json.JsonNamingPolicy.CamelCase"/> serialization turns
/// <see cref="UserNKey"/> into the wire's <c>userNKey</c> (verified by a schema-fixture
/// test rather than assumed). A property serializes only when non-null
/// (<see cref="JsonIgnoreCondition.WhenWritingNull"/>) so optional doc-09 fields are
/// genuinely absent, not present-as-<c>null</c>, matching the doc-09 field table's
/// "absent when ..." wording and keeping every emitted payload minimal and secret-free.
/// </summary>
/// <param name="Timestamp">Decision time, UTC, serialized as RFC 3339 (doc 09).</param>
/// <param name="Outcome">The wire outcome string: <c>"success"</c> or <c>"failure"</c>.</param>
/// <param name="Reason">The wire reason code (<see cref="AuthCallout.Contracts.Decisions.EventReasonWireFormat"/>).</param>
/// <param name="Application">The selected application's name, or null when none was selected.</param>
/// <param name="Profile">The matched profile's name, present on success only, or null.</param>
/// <param name="Principal">The <c>sub</c>/<c>azp</c>-style principal identifier, never the raw token, or null.</param>
/// <param name="Issuer">The token's original OIDC issuer, or null.</param>
/// <param name="UserNKey">The connection's user NKey, or null when unavailable to the caller.</param>
public sealed record AuthOutcomeEventPayload(
    DateTimeOffset Timestamp,
    string Outcome,
    string Reason,
    string? Application,
    string? Profile,
    string? Principal,
    string? Issuer,
    string? UserNKey)
{
    /// <summary>Gets the fixed <c>schemaVersion</c> for every payload this type produces (doc 09 §1).</summary>
    /// <value>Always <c>1</c>.</value>
    public int SchemaVersion => 1;
}
