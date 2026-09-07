// -----------------------------------------------------------------------
// <copyright file="SecretRefKind.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Primitives;

/// <summary>
/// The discriminator for <see cref="SecretRef"/>, per the secrets model in
/// <c>docs/design/04-configuration.md</c> §Secrets and ADR 0005.
/// </summary>
public enum SecretRefKind
{
    /// <summary>The secret value is embedded directly in plain config. Dev/local use only.</summary>
    Inline,

    /// <summary>The secret is read from a mounted file's contents.</summary>
    File,

    /// <summary>The secret is read from a Kubernetes Secret (mounted-volume convention; see ADR 0005 / P03).</summary>
    K8sSecret,

    /// <summary>The secret is fetched from a vault. Registered but not implemented in v1 (P03).</summary>
    Vault,
}
