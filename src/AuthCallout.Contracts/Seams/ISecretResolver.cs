// -----------------------------------------------------------------------
// <copyright file="ISecretResolver.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// Resolves a <see cref="SecretRef"/> to its plaintext value (doc 04 §Secrets, ADR 0005).
/// Implemented by <c>AuthCallout.Secrets</c> (P03); consumed once at startup by
/// <c>AuthCallout.Host</c> (P13) to resolve the bootstrap layer's seeds and, for the
/// Postgres source, its connection string — resolution is a startup-time-only concern
/// (the bootstrap layer is never hot-reloaded, doc 04), so this interface has no caching
/// or rotation surface.
/// </summary>
public interface ISecretResolver
{
    /// <summary>
    /// Resolves one secret reference. Fails closed: an unresolvable reference (missing
    /// file, missing Kubernetes Secret mount, an unimplemented <see cref="SecretRefKind.Vault"/>)
    /// throws rather than returning a placeholder, and the resulting exception message
    /// names the *locator* (path/name/key), never the value.
    /// </summary>
    /// <param name="secretRef">The secret reference to resolve.</param>
    /// <param name="cancellationToken">Cancellation for the resolution attempt.</param>
    /// <returns>The resolved, redacting secret wrapper.</returns>
    Task<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken);
}
