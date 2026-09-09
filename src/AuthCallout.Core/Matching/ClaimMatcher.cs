// -----------------------------------------------------------------------
// <copyright file="ClaimMatcher.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Core.Matching;

/// <summary>
/// Evaluates a <see cref="ClaimMatch"/> predicate against validated token claims
/// (<c>docs/design/02-authorization-model.md</c> §Claim match, ADR 0003). Shared by
/// application selection and profile selection (ADR 0001/0002): the same evaluator, applied
/// to different predicates at different resolution stages.
/// </summary>
/// <remarks>
/// Semantics (doc 02, pinned exactly):
/// <list type="bullet">
/// <item><description><b>OR within a key:</b> a predicate key matches if the claim's values
/// (via <see cref="IClaimsAccessor.GetValues"/>) contain <i>any</i> of the predicate's listed
/// values (ordinal string comparison).</description></item>
/// <item><description><b>AND across keys:</b> every predicate key must match for the overall
/// match to succeed.</description></item>
/// <item><description>An empty predicate list under a key never matches (there is nothing to
/// satisfy the OR), and a claim path with zero values never matches any non-empty value list.</description></item>
/// <item><description>An empty/absent <see cref="ClaimMatch"/> (<see cref="ClaimMatch.IsEmpty"/>)
/// always matches: whether an empty match is a <i>valid configuration</i> in a given context
/// (e.g. only for an application whose issuer alone already identifies it uniquely) is a
/// config-validation concern (P02's <c>ConfigValidator</c>), not this evaluator's.</description></item>
/// </list>
/// </remarks>
public static class ClaimMatcher
{
    /// <summary>Evaluates whether <paramref name="claims"/> satisfies <paramref name="match"/>.</summary>
    /// <param name="match">The claim-based predicate to evaluate.</param>
    /// <param name="claims">The validated claims to evaluate the predicate against.</param>
    /// <returns>True when every predicate key matches (AND across keys); true unconditionally for an empty match.</returns>
    public static bool IsMatch(ClaimMatch match, IClaimsAccessor claims)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(claims);

        foreach (var (claimPath, acceptedValues) in match.Predicates)
        {
            if (!MatchesKey(claimPath, acceptedValues, claims))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesKey(string claimPath, IReadOnlyList<string> acceptedValues, IClaimsAccessor claims)
    {
        if (acceptedValues.Count == 0)
        {
            return false;
        }

        var actualValues = claims.GetValues(claimPath);
        if (actualValues.Count == 0)
        {
            return false;
        }

        return acceptedValues.Any(accepted => actualValues.Contains(accepted, StringComparer.Ordinal));
    }
}
