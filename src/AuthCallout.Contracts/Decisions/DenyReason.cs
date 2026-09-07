// -----------------------------------------------------------------------
// <copyright file="DenyReason.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Decisions;

/// <summary>
/// The fine-grained, internal reason a connection was denied (doc 03 §Stages, ADR 0003
/// §Decision tree, ADR 0009 §Templates and expansion, D15/D19). This is the pipeline's
/// own vocabulary — richer than the wire event's <see cref="EventReason"/> — used for logs,
/// metrics, and the CRD/reload <c>status.reason</c> surfaces. <see cref="DenyReasonMap"/>
/// is the total, exhaustively-tested mapping from every member here to its
/// <see cref="EventReason"/> wire code (QUESTIONS C12: two enums + mapping, never a second
/// independent reason vocabulary).
/// </summary>
public enum DenyReason
{
    /// <summary>
    /// Reserved for a lower layer expecting a token where none was supplied (e.g. a
    /// misrouted call into <c>ITokenValidator</c>). The resolution pipeline itself never
    /// produces this for a genuinely tokenless connection — "no token" routes to the
    /// anonymous composer (doc 03 Stage 0), whose own empty-union case is
    /// <see cref="AnonymousNoContributions"/> (D15), not this reason.
    /// </summary>
    NoToken,

    /// <summary>A token was presented but its (unvalidated) <c>iss</c> matches no configured application (doc 03 Stage 1).</summary>
    UnknownIssuer,

    /// <summary>Token signature verification against the issuer's JWKS failed (doc 03 Stage 2, ADR 0016).</summary>
    SignatureInvalid,

    /// <summary>The token's <c>exp</c>/<c>nbf</c> failed lifetime validation, outside the issuer's clock skew (doc 03 Stage 2, ADR 0016).</summary>
    Expired,

    /// <summary>The token's <c>aud</c> did not match the issuer's configured audiences while <c>validateAudience</c> is true (ADR 0016).</summary>
    AudienceMismatch,

    /// <summary>The token's signing algorithm is not in the issuer's <c>allowedAlgorithms</c> allow-list, or is <c>HS*</c>/<c>none</c> (ADR 0016).</summary>
    AlgorithmRejected,

    /// <summary>The token validated, but zero applications on its issuer matched (doc 03 Stage 3).</summary>
    NoAppMatch,

    /// <summary>The token validated, but more than one application on its issuer matched, with no tie-breaker (doc 03 Stage 3, ADR 0003).</summary>
    AmbiguousApp,

    /// <summary>Exactly one application matched, but no profile within it matched, in order (doc 03 Stage 4, ADR 0001).</summary>
    NoProfileMatch,

    /// <summary>A <c>required</c> template variable resolved to no value (ADR 0009 §Templates and expansion).</summary>
    RequiredVariableMissing,

    /// <summary>A template variable's transformed value was still unsafe after a <c>require-safe</c> step (ADR 0009).</summary>
    UnsafeTemplateValue,

    /// <summary>Neither the profile's <c>nameClaim</c> nor the <c>sub</c> fallback resolved to a value (D19).</summary>
    NameClaimError,

    /// <summary>No token was presented and the anonymous union grant had zero contributions across all configured applications (D15).</summary>
    AnonymousNoContributions,
}
