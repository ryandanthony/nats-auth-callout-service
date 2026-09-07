// -----------------------------------------------------------------------
// <copyright file="SecretRefShape.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Contracts.Validation;

/// <summary>
/// Structural well-formedness checks for a <see cref="SecretRef"/>: that the locator
/// fields present match the ones its <see cref="SecretRefKind"/> requires. This is a
/// defense-in-depth check against a record constructed (e.g. via a <c>with</c> expression)
/// outside <see cref="SecretRef"/>'s own static factories, which already enforce these
/// invariants at construction time. It does **not** verify the referenced secret actually
/// resolves — that requires I/O and is P03's (<c>ISecretResolver</c>) responsibility at
/// startup; doc 04's "all secret references resolve" validation rule is therefore split:
/// shape here, resolvability at P03 runtime.
/// </summary>
internal static class SecretRefShape
{
    /// <summary>Checks whether <paramref name="secretRef"/>'s locator fields are consistent with its kind.</summary>
    internal static bool IsWellFormed(SecretRef secretRef) => secretRef.Kind switch
    {
        SecretRefKind.Inline => !string.IsNullOrEmpty(secretRef.Value),
        SecretRefKind.File => !string.IsNullOrEmpty(secretRef.Path),
        SecretRefKind.K8sSecret => !string.IsNullOrEmpty(secretRef.Name) && !string.IsNullOrEmpty(secretRef.Key),
        SecretRefKind.Vault => !string.IsNullOrEmpty(secretRef.Name),
        _ => false,
    };
}
