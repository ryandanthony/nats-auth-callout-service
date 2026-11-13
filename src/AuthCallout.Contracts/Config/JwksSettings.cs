// -----------------------------------------------------------------------
// <copyright file="JwksSettings.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// Bootstrap-level JWKS cache manager settings (doc 04 §Bootstrap <c>jwks</c>, doc 08
/// §JWKS cache manager). Governs how the <c>JwksCacheManager</c> (P06) keeps every
/// configured issuer's keys warm.
/// </summary>
/// <param name="RefreshInterval">Default proactive re-fetch cadence per issuer; overridable per issuer via <see cref="IssuerConfig.RefreshInterval"/>.</param>
/// <param name="FetchTimeout">Hard timeout on any single discovery/JWKS fetch (fail closed).</param>
/// <param name="PrewarmRequired">When true (default), every configured issuer must prewarm before <c>Serving</c>; when false, prewarm is best-effort.</param>
/// <param name="AllowInsecureHttp">
/// Dev/test only (default false): allow discovery and JWKS documents to be fetched over plain
/// <c>http://</c>, for a containerized callout against a plain-HTTP dev IdP (issue #2). Only the
/// transport check on those document fetches is relaxed; token validation (signature,
/// <c>iss</c>, <c>aud</c>, <c>exp</c>, algorithm allow-list, ADR 0016) is unchanged. The host
/// logs a warning at startup when this is on.
/// </param>
public sealed record JwksSettings(DurationString RefreshInterval, DurationString FetchTimeout, bool PrewarmRequired, bool AllowInsecureHttp = false);
