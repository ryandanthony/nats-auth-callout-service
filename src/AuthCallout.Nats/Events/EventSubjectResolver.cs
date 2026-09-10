// -----------------------------------------------------------------------
// <copyright file="EventSubjectResolver.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;

namespace AuthCallout.Nats.Events;

/// <summary>
/// Resolves which NATS subject (if any) an outcome event publishes to (doc 05 §Auth outcome
/// events, doc 03 §Outcome events, ADR 0008, P09 task 2).
/// <para>
/// Routing rule, exactly per doc 05's flowchart ("app success" / "app failure" /
/// "anonymous → global anon subject if set"):
/// </para>
/// <list type="bullet">
/// <item><description>
/// A <see cref="GrantedDecision"/> with an <see cref="Decision.Application"/> selected is an
/// app success: <see cref="EventSubjects.Success"/> on that application's
/// <see cref="Application.Events"/>.
/// </description></item>
/// <item><description>
/// A <see cref="GrantedDecision"/> with no application (anonymous, ADR 0004) uses the
/// bootstrap-global <see cref="AnonymousGlobalConfig.Events"/>'s
/// <see cref="EventSubjects.Success"/>. There is no default: absent config means no event
/// (ADR 0008: "off unless a subject is set").
/// </description></item>
/// <item><description>
/// A <see cref="DeniedDecision"/> with an <see cref="Decision.Application"/> selected (denied
/// at or after Stage 4 profile matching, or grant construction) is an app failure: that
/// application's <see cref="EventSubjects.Failure"/>.
/// </description></item>
/// <item><description>
/// A <see cref="DeniedDecision"/> with no application — denied before app selection
/// (unknown issuer, invalid token/signature/expiry/algorithm/audience, no app match,
/// ambiguous app, or the anonymous-no-contributions case) — has no per-app subject to use,
/// so it falls back to the global anonymous <see cref="EventSubjects.Failure"/> if
/// configured (doc 05's exact wording: "anonymous → global anon subject if set" is the only
/// non-per-app subject source the design names; pinned here per the packet's own escalation
/// guidance — "if ambiguous, escalate to coordinator" — this reading is unambiguous from the
/// flowchart, which has exactly three branches: app-success, app-failure, anonymous).
/// </description></item>
/// </list>
/// In every branch, a null/absent subject means <b>no event</b>, silently (ADR 0008: "when
/// no subject is configured, no event is published").
/// </summary>
public static class EventSubjectResolver
{
    /// <summary>Resolves the publish subject for a decision against the active config, or null to skip.</summary>
    /// <param name="decision">The resolution outcome to route.</param>
    /// <param name="routing">The active routing aggregate (for the selected application's event subjects, if any).</param>
    /// <param name="anonymousEvents">The bootstrap-global anonymous event subjects (ADR 0008), or null when unset.</param>
    /// <returns>The subject to publish to, or null when no subject is configured for this outcome.</returns>
    public static string? Resolve(Decision decision, RoutingConfig routing, EventSubjects? anonymousEvents)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(routing);

        var isSuccess = decision is GrantedDecision;
        if (decision.Application is { } applicationName)
        {
            var application = FindApplication(routing, applicationName);
            var appSubjects = application?.Events;
            return isSuccess ? appSubjects?.Success : appSubjects?.Failure;
        }

        return isSuccess ? anonymousEvents?.Success : anonymousEvents?.Failure;
    }

    private static Application? FindApplication(RoutingConfig routing, string applicationName) =>
        routing.Applications.FirstOrDefault(application => string.Equals(application.Name, applicationName, StringComparison.Ordinal));
}
