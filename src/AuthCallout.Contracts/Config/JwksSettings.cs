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
public sealed record JwksSettings(DurationString RefreshInterval, DurationString FetchTimeout, bool PrewarmRequired);
