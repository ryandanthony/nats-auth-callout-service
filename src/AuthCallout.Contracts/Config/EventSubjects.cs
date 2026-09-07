// -----------------------------------------------------------------------
// <copyright file="EventSubjects.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Config;

/// <summary>
/// Explicit auth-outcome event subjects for an application or the global anonymous config
/// (ADR 0008, ADR 0007). Either may be null to publish only one side; both null (or the
/// whole block omitted) disables events for that scope. There is no templated <c>{outcome}</c>
/// form — subjects are explicit strings only.
/// </summary>
/// <param name="Success">Subject for successful auth outcomes, or null to not publish successes.</param>
/// <param name="Failure">Subject for failed/denied auth outcomes, or null to not publish failures.</param>
public sealed record EventSubjects(string? Success, string? Failure);
