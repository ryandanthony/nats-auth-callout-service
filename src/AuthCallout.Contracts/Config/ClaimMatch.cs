// -----------------------------------------------------------------------
// <copyright file="ClaimMatch.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Internal;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// A claim-based predicate shared by application selection and profile selection
/// (<c>docs/design/02-authorization-model.md</c> §Claim match, ADR 0003). A predicate is a
/// map of claim path (nested paths like <c>realm_access.roles</c> supported) to accepted
/// values. Evaluation semantics (not enforced here — this is a data holder; the matcher
/// implementation is P04's <c>ClaimMatcher</c> in <c>AuthCallout.Core</c>):
/// <list type="bullet">
/// <item><description>Array claims: a path matches if the claim's values contain any listed value (OR within a key).</description></item>
/// <item><description>Multiple keys must all match (AND across keys).</description></item>
/// <item><description>An empty/absent match is only valid for an application whose issuer alone already identifies it uniquely (validated by the aggregate validator).</description></item>
/// </list>
/// </summary>
public sealed record ClaimMatch(IReadOnlyDictionary<string, IReadOnlyList<string>> Predicates)
{
    /// <summary>Gets an empty match (no predicates) — valid only where the caller's context makes it unambiguous.</summary>
    /// <value>An empty match (no predicates).</value>
    public static ClaimMatch Empty { get; } = new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    /// <summary>Gets a value indicating whether this match declares no predicates.</summary>
    /// <value>True when this match declares no predicates.</value>
    public bool IsEmpty => Predicates.Count == 0;

    /// <inheritdoc />
    public bool Equals(ClaimMatch? other) =>
        other is not null && StructuralEquality.DictionaryOfListsEqual(Predicates, other.Predicates);

    /// <inheritdoc />
    public override int GetHashCode() => StructuralEquality.DictionaryOfListsHashCode(Predicates);
}
