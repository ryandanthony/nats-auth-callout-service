// -----------------------------------------------------------------------
// <copyright file="TemplateVariable.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Internal;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// A named template variable: a placeholder bound to a claim, with an ordered transform
/// pipeline (ADR 0009). Referenced by <c>{name}</c> in a profile's
/// <see cref="Profile.PublishTemplates"/>/<see cref="Profile.SubscribeTemplates"/>.
/// </summary>
/// <param name="Name">The <c>{name}</c> placeholder used in templates.</param>
/// <param name="Claim">The claim path this variable reads (nested paths supported, as in <see cref="ClaimMatch"/>).</param>
/// <param name="Transforms">Ordered transform steps applied left to right to each resolved value.</param>
/// <param name="Required">When true, the variable must resolve to at least one value or the connection is denied (<see cref="Decisions.DenyReason.RequiredVariableMissing"/>).</param>
public sealed record TemplateVariable(string Name, string Claim, IReadOnlyList<Transform> Transforms, bool Required)
{
    /// <inheritdoc />
    public bool Equals(TemplateVariable? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Claim, other.Claim, StringComparison.Ordinal)
            && Required == other.Required
            && StructuralEquality.SequenceEqual(Transforms, other.Transforms);
    }

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Name, Claim, Required, StructuralEquality.SequenceHashCode(Transforms));
}
