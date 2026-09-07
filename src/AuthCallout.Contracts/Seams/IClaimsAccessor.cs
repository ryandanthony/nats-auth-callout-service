// -----------------------------------------------------------------------
// <copyright file="IClaimsAccessor.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// Read-only access over a validated token's claims, supporting nested paths and array
/// claims (doc 02 §Claim match: <c>realm_access.roles</c>-style nesting, array-contains
/// semantics). Implemented by <c>AuthCallout.Core</c>'s <c>ClaimsAccessor</c> (P04);
/// consumed by the claim matcher and template-variable resolver (P04) and, transitively,
/// by the resolution pipeline (P05) via <see cref="ITokenValidator"/>'s output.
/// </summary>
public interface IClaimsAccessor
{
    /// <summary>
    /// Reads every value at a claim path (nested paths supported, e.g. <c>realm_access.roles</c>).
    /// A scalar claim yields a single-element sequence; an array claim yields one element
    /// per array entry (numbers/bools coerced to invariant-culture strings; nested objects
    /// contribute no value, per P04's pinned coercion rules); an absent path yields an
    /// empty sequence.
    /// </summary>
    /// <param name="claimPath">The claim path to read (nested paths supported).</param>
    /// <returns>Every value at the path; empty when absent.</returns>
    IReadOnlyList<string> GetValues(string claimPath);

    /// <summary>
    /// Reads a single-valued claim (the first value at the path, if any). Convenience over
    /// <see cref="GetValues"/> for claims known to be scalar (e.g. <c>sub</c>, a profile's
    /// <c>nameClaim</c>). Returns null when the path is absent.
    /// </summary>
    /// <param name="claimPath">The claim path to read (nested paths supported).</param>
    /// <returns>The first value at the path, or null when absent.</returns>
    string? GetSingleValue(string claimPath);

    /// <summary>True when the claim path is present (with at least one value).</summary>
    /// <param name="claimPath">The claim path to check.</param>
    /// <returns>True when the path resolves to at least one value.</returns>
    bool HasClaim(string claimPath);
}
