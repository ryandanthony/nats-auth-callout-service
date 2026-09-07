// -----------------------------------------------------------------------
// <copyright file="TokenValidationOutcome.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Decisions;

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// The outcome of <see cref="ITokenValidator.ValidateAsync"/>: either the token's validated
/// claim set, or the specific <see cref="DenyReason"/> validation failed with. Never both.
/// </summary>
/// <param name="Claims">The validated claims, present only when <see cref="Reason"/> is null.</param>
/// <param name="Reason">The validation failure reason, present only when <see cref="Claims"/> is null.</param>
public sealed record TokenValidationOutcome(IClaimsAccessor? Claims, DenyReason? Reason)
{
    /// <summary>Creates a successful outcome carrying the validated claims.</summary>
    /// <param name="claims">The validated claims.</param>
    /// <returns>A successful outcome.</returns>
    public static TokenValidationOutcome Valid(IClaimsAccessor claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        return new TokenValidationOutcome(claims, Reason: null);
    }

    /// <summary>Creates a failed outcome carrying the specific validation <see cref="DenyReason"/>.</summary>
    /// <param name="reason">The specific validation failure reason.</param>
    /// <returns>A failed outcome.</returns>
    public static TokenValidationOutcome Invalid(DenyReason reason) => new(Claims: null, reason);

    /// <summary>Gets a value indicating whether validation succeeded and <see cref="Claims"/> is populated.</summary>
    /// <value>True when validation succeeded.</value>
    public bool IsValid => Claims is not null;
}
