// -----------------------------------------------------------------------
// <copyright file="DiscoveryIdentityPropagation.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>The <c>identityPropagation</c> section of a <see cref="DiscoveryDescriptor"/> (doc 09 §2).</summary>
/// <param name="Enabled">Master toggle (ADR 0012).</param>
/// <param name="Header">Message header name carrying the identity assertion. Present when enabled.</param>
/// <param name="OidcTokenBucket">Physical OIDC token-store bucket name (ADR 0013). Present when enabled.</param>
/// <param name="AssertionBucket">Physical assertions bucket name (ADR 0013). Present when enabled.</param>
/// <param name="AssertionKeysBucket">Physical assertion-keys bucket name (ADR 0013). Present when enabled.</param>
/// <param name="CurrentKid">The active assertion signing key's <c>kid</c> (ADR 0014). Present when enabled.</param>
public sealed record DiscoveryIdentityPropagation(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("header")] string? Header,
    [property: JsonPropertyName("oidcTokenBucket")] string? OidcTokenBucket,
    [property: JsonPropertyName("assertionBucket")] string? AssertionBucket,
    [property: JsonPropertyName("assertionKeysBucket")] string? AssertionKeysBucket,
    [property: JsonPropertyName("currentKid")] string? CurrentKid);
