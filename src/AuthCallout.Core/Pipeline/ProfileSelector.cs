// -----------------------------------------------------------------------
// <copyright file="ProfileSelector.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;
using AuthCallout.Core.Matching;

namespace AuthCallout.Core.Pipeline;

/// <summary>
/// Stage 4 of the resolution pipeline (doc 03 §Profile selection, ADR 0001): selects the
/// first profile, in the selected application's declared order, whose <see cref="Profile.Match"/>
/// is satisfied. First-match-wins; order is the sole tie-breaker and multiple matches are
/// never an error (ADR 0001: "order is the tie-breaker; multiple matches are not an error").
/// </summary>
internal static class ProfileSelector
{
    /// <summary>Selects the first matching profile in <paramref name="profiles"/>, in order.</summary>
    /// <param name="profiles">The application's ordered profile list.</param>
    /// <param name="claims">The token's validated claims.</param>
    /// <returns>The first matching profile, or null when none matched (doc 03 Stage 4: "No profile matches → deny").</returns>
    internal static Profile? SelectFirstMatch(IReadOnlyList<Profile> profiles, IClaimsAccessor claims) =>
        profiles.FirstOrDefault(profile => ClaimMatcher.IsMatch(profile.Match, claims));
}
