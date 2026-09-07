// -----------------------------------------------------------------------
// <copyright file="RoutingConfigBatch.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// One successfully validated routing batch emitted by an <see cref="IRoutingConfigProvider"/>
/// (doc 04 §Provider abstraction: "load the current set, aggregate into the canonical
/// model, validate, and notify the core on change"). The provider only ever emits a batch
/// that already passed <c>ConfigValidator.ValidateRouting</c>; a batch that fails
/// validation is never emitted (ADR 0006 — ownership of "invalid → keep last-good" lives
/// with the config-apply loop in P13, not the provider, but the provider itself never
/// hands the loop something it knows is invalid).
/// </summary>
/// <param name="Routing">The validated routing aggregate.</param>
/// <param name="ObservedAt">When this batch was produced, used to update the source's <c>LastSuccessfulSync</c> heartbeat (ADR 0017).</param>
public sealed record RoutingConfigBatch(RoutingConfig Routing, DateTimeOffset ObservedAt);
