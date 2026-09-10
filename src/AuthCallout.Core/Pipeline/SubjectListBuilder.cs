// -----------------------------------------------------------------------
// <copyright file="SubjectListBuilder.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Core.Pipeline;

/// <summary>
/// Small ordered, de-duplicated subject-list accumulation helpers shared by
/// <see cref="GrantBuilder"/> (profile grants) and <see cref="AnonymousComposer"/> (the
/// anonymous union, ADR 0004: "identical subjects contributed by multiple applications are
/// de-duplicated"). Preserves first-seen order rather than sorting, so a grant's subject
/// order is a direct, readable trace of how it was assembled (static subjects, then
/// template-expanded subjects, then auto-added KV reads in their fixed doc 03 order).
/// </summary>
internal static class SubjectListBuilder
{
    /// <summary>Appends every subject in <paramref name="subjects"/> to <paramref name="target"/> that has not already been added (per <paramref name="seen"/>).</summary>
    /// <param name="target">The accumulating ordered subject list.</param>
    /// <param name="seen">The set of subjects already added to <paramref name="target"/> (ordinal comparison).</param>
    /// <param name="subjects">The subjects to append, skipping any already present.</param>
    internal static void AddRange(List<string> target, HashSet<string> seen, IReadOnlyList<string> subjects)
    {
        foreach (var subject in subjects)
        {
            AddOne(target, seen, subject);
        }
    }

    /// <summary>Appends one subject to <paramref name="target"/> if it has not already been added.</summary>
    /// <param name="target">The accumulating ordered subject list.</param>
    /// <param name="seen">The set of subjects already added to <paramref name="target"/> (ordinal comparison).</param>
    /// <param name="subject">The subject to append if new.</param>
    internal static void AddOne(List<string> target, HashSet<string> seen, string subject)
    {
        if (seen.Add(subject))
        {
            target.Add(subject);
        }
    }
}
