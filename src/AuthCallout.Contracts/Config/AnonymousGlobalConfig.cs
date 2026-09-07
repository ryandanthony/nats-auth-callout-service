// -----------------------------------------------------------------------
// <copyright file="AnonymousGlobalConfig.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// The global anonymous-grant configuration (ADR 0004, ADR 0007). Anonymous connections
/// carry no token and are minted into a dedicated, globally configured account with a
/// global expiry, since the anonymous union grant may span applications targeting
/// different accounts.
/// </summary>
/// <param name="TargetAccount">The NATS account anonymous connections are minted into.</param>
/// <param name="Expiry">The sole expiry source for anonymous grants, which carry no token <c>exp</c>.</param>
/// <param name="Events">Optional auth outcome event subjects for anonymous outcomes. Null disables anonymous event publishing (there is no default subject, ADR 0008).</param>
public sealed record AnonymousGlobalConfig(string TargetAccount, DurationString Expiry, EventSubjects? Events);
