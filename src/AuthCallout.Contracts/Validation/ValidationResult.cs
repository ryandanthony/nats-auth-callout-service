// -----------------------------------------------------------------------
// <copyright file="ValidationResult.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Internal;

namespace AuthCallout.Contracts.Validation;

/// <summary>
/// The outcome of validating a <see cref="Config.RoutingConfig"/> or
/// <see cref="Config.BootstrapConfig"/> (doc 04 §Validation, ADR 0006 §Steady-state
/// failure: "a single offending unit blocks the entire batch"). Never partially valid:
/// either <see cref="IsValid"/> and <see cref="Errors"/> is empty, or invalid and every
/// violated rule is listed (all violations reported at once, not fail-fast on the first).
/// </summary>
/// <param name="Errors">Every rule violated by the input, in a stable, deterministic order. Empty when valid.</param>
public sealed record ValidationResult(IReadOnlyList<ValidationError> Errors)
{
    /// <summary>Gets the canonical valid result (no errors).</summary>
    /// <value>The canonical valid result.</value>
    public static ValidationResult Valid { get; } = new([]);

    /// <summary>Gets a value indicating whether no rule was violated.</summary>
    /// <value>True when no rule was violated.</value>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Creates a result from one or more errors.</summary>
    /// <param name="errors">The rule violations to report. Empty produces <see cref="Valid"/>.</param>
    /// <returns>The resulting validation result.</returns>
    public static ValidationResult Invalid(IEnumerable<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var list = errors.ToArray();
        return list.Length == 0 ? Valid : new ValidationResult(list);
    }

    /// <inheritdoc />
    public bool Equals(ValidationResult? other) =>
        other is not null && StructuralEquality.SequenceEqual(Errors, other.Errors);

    /// <inheritdoc />
    public override int GetHashCode() => StructuralEquality.SequenceHashCode(Errors);
}
