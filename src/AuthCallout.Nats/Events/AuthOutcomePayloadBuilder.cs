// -----------------------------------------------------------------------
// <copyright file="AuthOutcomePayloadBuilder.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Decisions;

namespace AuthCallout.Nats.Events;

/// <summary>
/// Builds the v1 auth outcome payload from a resolved <see cref="Decision"/> (ADR 0008, doc
/// 09 §1, P09 task 1). A pure function of its inputs, unit-tested directly without any NATS
/// or config dependency.
/// </summary>
public static class AuthOutcomePayloadBuilder
{
    private const string SuccessOutcome = "success";
    private const string FailureOutcome = "failure";

    /// <summary>
    /// Builds the payload for one decision. <see cref="AuthOutcomeEventPayload.Outcome"/> and
    /// <see cref="AuthOutcomeEventPayload.Reason"/> come from the type of <paramref name="decision"/>:
    /// a <see cref="GrantedDecision"/> with no <see cref="Decision.Application"/> is the
    /// anonymous case (<see cref="EventReason.Anonymous"/>, D15/ADR 0004); a
    /// <see cref="GrantedDecision"/> with an application is <see cref="EventReason.Granted"/>;
    /// a <see cref="DeniedDecision"/>'s wire reason comes from
    /// <see cref="DenyReasonMap.ToEventReason"/> (QUESTIONS C12, covers
    /// <c>required-variable-missing</c> and <c>unsafe-template-value</c> under the single
    /// wire code <see cref="EventReason.GrantConstructionFailed"/>, ADR 0009). Every field is
    /// secret-free by construction: it only ever reads <see cref="Decision"/>'s own
    /// already-secret-free fields (ADR 0008: "never the raw token") plus the caller-supplied
    /// <paramref name="userNKey"/>.
    /// </summary>
    /// <param name="decision">The resolution outcome to build a payload for.</param>
    /// <param name="userNKey">The connecting user's NKey, or null when unavailable to the caller.</param>
    /// <param name="timestamp">The decision time (UTC).</param>
    /// <returns>The built payload, ready to serialize.</returns>
    public static AuthOutcomeEventPayload Build(Decision decision, string? userNKey, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var (outcome, reason) = decision switch
        {
            GrantedDecision { Application: null } => (SuccessOutcome, EventReason.Anonymous),
            GrantedDecision => (SuccessOutcome, EventReason.Granted),
            DeniedDecision denied => (FailureOutcome, denied.Reason.ToEventReason()),
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unrecognized Decision subtype; expected GrantedDecision or DeniedDecision."),
        };

        var profile = decision is GrantedDecision granted ? granted.ProfileName : null;

        return new AuthOutcomeEventPayload(
            Timestamp: timestamp,
            Outcome: outcome,
            Reason: reason.ToWireString(),
            Application: decision.Application,
            Profile: profile,
            Principal: decision.Principal,
            Issuer: decision.Issuer,
            UserNKey: userNKey);
    }
}
