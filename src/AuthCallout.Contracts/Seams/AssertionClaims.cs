// -----------------------------------------------------------------------
// <copyright file="AssertionClaims.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// The claims bound into one identity assertion (ADR 0011 §The assertion). Carries no raw
/// OIDC token and no secrets.
/// </summary>
/// <param name="Id">The callout-generated connection id (ADR 0010) — the key into the token store.</param>
/// <param name="Subject">The token's <c>sub</c> claim.</param>
/// <param name="Application">The application slug the connection was authorized against.</param>
/// <param name="Issuer">The original OIDC issuer authority.</param>
/// <param name="Expiry">The assertion's expiry, aligned to the token/grant lifetime.</param>
public sealed record AssertionClaims(string Id, string Subject, string Application, string Issuer, DateTimeOffset Expiry);
