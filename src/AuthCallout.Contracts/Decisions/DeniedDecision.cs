// -----------------------------------------------------------------------
// <copyright file="DeniedDecision.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Decisions;

/// <summary>
/// A denied resolution (doc 03 §Stages' deny points, ADR 0003 §Decision tree). Every deny
/// path in the pipeline terminates here with a specific <see cref="Reason"/>; the minter
/// (P07) turns this into a generic-detail authorization error response (no internal detail
/// leak to the client), while <see cref="Detail"/> and <see cref="Reason"/> go to logs,
/// metrics, and the outcome event (via <see cref="DenyReasonMap"/>) only.
/// </summary>
/// <param name="Application">The application name, if one had been selected before the denial occurred (e.g. <see cref="DenyReason.NoProfileMatch"/>); otherwise null.</param>
/// <param name="Principal">The <c>sub</c>/<c>azp</c>-style principal identifier, if validated claims were available before the denial; otherwise null.</param>
/// <param name="Issuer">The token's issuer, if one was read before the denial; otherwise null.</param>
/// <param name="Reason">The specific, internal reason for the denial.</param>
/// <param name="Detail">Optional free-text detail for logs (never surfaced to the client; never secret material).</param>
public sealed record DeniedDecision(string? Application, string? Principal, string? Issuer, DenyReason Reason, string? Detail = null)
    : Decision(Application, Principal, Issuer);
