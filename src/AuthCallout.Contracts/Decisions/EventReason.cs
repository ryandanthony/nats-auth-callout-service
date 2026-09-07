// -----------------------------------------------------------------------
// <copyright file="EventReason.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Decisions;

/// <summary>
/// The wire-stable auth-outcome event <c>reason</c> codes, exactly per
/// <c>docs/design/09-schemas-and-versioning.md</c> §1's field table (ADR 0008, ADR 0015).
/// This is a coarser vocabulary than <see cref="DenyReason"/> by design: new codes are
/// additive (no <c>schemaVersion</c> bump) and consumers treat unknown codes as generic
/// failure, so the wire enum only grows for genuinely new, externally meaningful outcomes,
/// not every internal denial nuance. <see cref="DenyReasonMap"/> maps every
/// <see cref="DenyReason"/> onto one of these.
/// </summary>
public enum EventReason
{
    /// <summary>An anonymous grant was minted (<c>outcome: success</c>).</summary>
    Anonymous,

    /// <summary>The presented token failed validation (signature, expiry, algorithm, audience).</summary>
    InvalidToken,

    /// <summary>The token's issuer matches no configured application.</summary>
    NoIssuerMatch,

    /// <summary>Zero applications matched among the issuer's candidates.</summary>
    NoAppMatch,

    /// <summary>More than one application matched among the issuer's candidates.</summary>
    AmbiguousApp,

    /// <summary>No profile matched within the selected application.</summary>
    NoProfileMatch,

    /// <summary>Grant construction failed: a required template variable was missing or a transformed value was unsafe (ADR 0009).</summary>
    GrantConstructionFailed,

    /// <summary>A non-anonymous grant was minted (<c>outcome: success</c>).</summary>
    Granted,
}
