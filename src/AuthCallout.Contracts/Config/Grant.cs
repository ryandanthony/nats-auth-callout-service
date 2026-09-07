// -----------------------------------------------------------------------
// <copyright file="Grant.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Internal;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// A pair of publish/subscribe subject-pattern lists — the unit of permission,
/// reused by profiles and by anonymous contributions
/// (<c>docs/design/02-authorization-model.md</c> §Grant, ADR 0007).
/// </summary>
public sealed record Grant(IReadOnlyList<string> Publish, IReadOnlyList<string> Subscribe)
{
    /// <summary>Gets a grant with no subjects.</summary>
    /// <value>A grant with empty publish/subscribe lists.</value>
    public static Grant Empty { get; } = new([], []);

    /// <inheritdoc />
    public bool Equals(Grant? other) =>
        other is not null
        && StructuralEquality.SequenceEqual(Publish, other.Publish)
        && StructuralEquality.SequenceEqual(Subscribe, other.Subscribe);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            StructuralEquality.SequenceHashCode(Publish),
            StructuralEquality.SequenceHashCode(Subscribe));
}
