// -----------------------------------------------------------------------
// <copyright file="IdentityPropagationConfig.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Config;

/// <summary>
/// Per-application identity-propagation toggle (ADR 0012). Defaults to enabled. When
/// disabled, the callout writes no token/assertion records for the application and
/// <see cref="Profile.AutoAddSessionAccess"/> on any of its profiles is a config-load
/// error; keys-bucket and discovery reads are still added to every authenticated grant
/// regardless of the toggle.
/// </summary>
/// <param name="Enabled">The master toggle.</param>
public sealed record IdentityPropagationConfig(bool Enabled)
{
    /// <summary>Gets the default (<c>Enabled: true</c>) applied when an application omits this block.</summary>
    /// <value>The default identity-propagation config (enabled).</value>
    public static IdentityPropagationConfig Default { get; } = new(Enabled: true);
}
