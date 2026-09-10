// -----------------------------------------------------------------------
// <copyright file="AppSelector.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;
using AuthCallout.Core.Matching;

namespace AuthCallout.Core.Pipeline;

/// <summary>
/// Stage 3 of the resolution pipeline (doc 03 §Application selection, ADR 0003): selects the
/// single application, among one issuer's candidates, whose claim-based <see cref="Application.Match"/>
/// is satisfied by the already-validated claims.
/// </summary>
internal static class AppSelector
{
    /// <summary>
    /// Selects an application among <paramref name="candidates"/> (all sharing one issuer
    /// authority, per <see cref="RoutingConfig.IssuerIndex"/>).
    /// </summary>
    /// <remarks>
    /// A candidate with a null <see cref="Application.Match"/> is treated as unconditionally
    /// matching, exactly like <see cref="ClaimMatch.Empty"/> (ADR 0003: "a single candidate
    /// with a unique issuer skips this step"; the aggregate validator's <c>ambiguous-null-match</c>
    /// rule guarantees a null match only ever occurs when it is the issuer's sole candidate,
    /// so this reduces to doc 03's stage-3 bullets without special-casing candidate count).
    /// </remarks>
    /// <param name="candidates">Every application configured for the token's (unvalidated) issuer.</param>
    /// <param name="claims">The token's validated claims.</param>
    /// <returns>The selected application, or the specific denial when zero or more than one candidate matched.</returns>
    internal static AppSelectionResult Select(IReadOnlyList<Application> candidates, IClaimsAccessor claims)
    {
        var matches = candidates
            .Where(candidate => candidate.Match is null || ClaimMatcher.IsMatch(candidate.Match, claims))
            .ToList();

        return matches.Count switch
        {
            0 => AppSelectionResult.Deny(DenyReason.NoAppMatch),
            1 => AppSelectionResult.Success(matches[0]),
            _ => AppSelectionResult.Deny(DenyReason.AmbiguousApp),
        };
    }

    /// <summary>The outcome of <see cref="Select"/>: either the selected application, or the specific denial.</summary>
    internal readonly record struct AppSelectionResult
    {
        private AppSelectionResult(Application? application, DenyReason? denyReason)
        {
            Application = application;
            DenyReason = denyReason;
        }

        /// <summary>Gets the selected application, present only when <see cref="DenyReason"/> is null.</summary>
        /// <value>The selected application, or null on denial.</value>
        internal Application? Application { get; }

        /// <summary>Gets the denial reason, present only when <see cref="Application"/> is null.</summary>
        /// <value>The denial reason, or null on success.</value>
        internal DenyReason? DenyReason { get; }

        /// <summary>Creates a successful selection.</summary>
        internal static AppSelectionResult Success(Application application) => new(application, null);

        /// <summary>Creates a denied selection.</summary>
        internal static AppSelectionResult Deny(DenyReason reason) => new(null, reason);
    }
}
