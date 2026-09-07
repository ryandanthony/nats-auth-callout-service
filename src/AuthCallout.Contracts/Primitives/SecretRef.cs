// -----------------------------------------------------------------------
// <copyright file="SecretRef.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Primitives;

/// <summary>
/// A reference to a sensitive value, resolved separately from plain config
/// (<c>docs/design/04-configuration.md</c> §Secrets, ADR 0005). Immutable,
/// value-equal. Construct via <see cref="Inline"/>, <see cref="FromFile"/>,
/// <see cref="FromK8sSecret"/>, or <see cref="Vault"/> so each kind's locator
/// invariants are enforced at construction rather than left to callers.
/// </summary>
/// <param name="Kind">Which resolution mechanism this reference uses.</param>
/// <param name="Value">Inline literal value. Set only for <see cref="SecretRefKind.Inline"/>.</param>
/// <param name="Path">
/// Mounted file path. Set for <see cref="SecretRefKind.File"/> and, when supplied, for
/// <see cref="SecretRefKind.Vault"/> (a vault mount path); null for the other kinds.
/// </param>
/// <param name="Name">
/// The Kubernetes Secret name (<see cref="SecretRefKind.K8sSecret"/>) or the vault secret
/// name (<see cref="SecretRefKind.Vault"/>); null for the other kinds.
/// </param>
/// <param name="Key">
/// The key within the Kubernetes Secret or vault secret; null for
/// <see cref="SecretRefKind.Inline"/> and <see cref="SecretRefKind.File"/>.
/// </param>
public sealed record SecretRef(SecretRefKind Kind, string? Value, string? Path, string? Name, string? Key)
{
    /// <summary>Creates an inline literal secret reference. Intended for dev/local use only (P03 logs a warning).</summary>
    /// <param name="value">The literal secret value.</param>
    /// <returns>The constructed inline secret reference.</returns>
    public static SecretRef Inline(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new SecretRef(SecretRefKind.Inline, value, Path: null, Name: null, Key: null);
    }

    /// <summary>Creates a mounted-file secret reference.</summary>
    /// <param name="path">The mounted file path to read at resolution time.</param>
    /// <returns>The constructed file secret reference.</returns>
    public static SecretRef FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new SecretRef(SecretRefKind.File, Value: null, path, Name: null, Key: null);
    }

    /// <summary>Creates a Kubernetes Secret reference, resolved via the mounted-volume convention (P03), not the k8s API.</summary>
    /// <param name="name">The Kubernetes Secret name.</param>
    /// <param name="key">The key within the Secret.</param>
    /// <returns>The constructed Kubernetes Secret reference.</returns>
#pragma warning disable S100 // "K8s" matches the repo-wide convention (SecretRefKind.K8sSecret, AuthCallout.Config.K8s); "K8S" would be inconsistent.
    public static SecretRef FromK8sSecret(string name, string key)
#pragma warning restore S100
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new SecretRef(SecretRefKind.K8sSecret, Value: null, Path: null, name, key);
    }

    /// <summary>Creates a vault secret reference. Registered as a kind but resolution is not implemented in v1 (P03 fails closed).</summary>
    /// <param name="name">The vault secret name.</param>
    /// <param name="key">The key within the vault secret, if applicable.</param>
    /// <param name="path">The vault mount path, if applicable.</param>
    /// <returns>The constructed vault secret reference.</returns>
    public static SecretRef Vault(string name, string? key = null, string? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new SecretRef(SecretRefKind.Vault, Value: null, path, name, key);
    }
}
