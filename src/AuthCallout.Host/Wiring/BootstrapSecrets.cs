// -----------------------------------------------------------------------
// <copyright file="BootstrapSecrets.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;
using AuthCallout.Identity.Assertions;

namespace AuthCallout.Host.Wiring;

/// <summary>Resolves the bootstrap layer's secret references (doc 08 task 1: "resolve secrets").</summary>
public static class BootstrapSecrets
{
    /// <summary>
    /// Resolves the issuer seed (required), the optional service-user seed, the optional
    /// xkey seed, and the assertion signing keyset (empty when unconfigured). Every
    /// <see cref="ResolvedSecret"/> is revealed and disposed immediately —
    /// the plaintext string this returns is the value <c>UserJwtMinter</c>/<c>NatsAuthOpts</c>/
    /// <c>CalloutCrypto</c>/<c>AssertionSigner</c> hold for the process lifetime, per their own constructors.
    /// </summary>
    /// <param name="bootstrap">The bootstrap config carrying the secret references.</param>
    /// <param name="secretResolver">The resolver to resolve each reference through.</param>
    /// <param name="cancellationToken">Cancellation for the resolution attempts.</param>
    /// <returns>The resolved plaintext secrets.</returns>
    public static async Task<ResolvedBootstrapSecrets> ResolveAsync(
        BootstrapConfig bootstrap, ISecretResolver secretResolver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(secretResolver);

        var issuerSeed = await RevealAsync(bootstrap.IssuerSeed, secretResolver, cancellationToken);
        var serviceUserSeed = bootstrap.ServiceUserSeed is { } serviceUserSeedRef
            ? await RevealAsync(serviceUserSeedRef, secretResolver, cancellationToken)
            : null;
        var xkeySeed = bootstrap.XkeySeed is { } xkeySeedRef
            ? await RevealAsync(xkeySeedRef, secretResolver, cancellationToken)
            : null;
        var assertionKeys = await ResolveAssertionKeysAsync(bootstrap.AssertionKeys, secretResolver, cancellationToken);

        return new ResolvedBootstrapSecrets(issuerSeed, serviceUserSeed, xkeySeed, assertionKeys);
    }

    private static async Task<IReadOnlyList<ResolvedAssertionKey>> ResolveAssertionKeysAsync(
        IReadOnlyList<AssertionKey>? assertionKeys, ISecretResolver secretResolver, CancellationToken cancellationToken)
    {
        if (assertionKeys is null || assertionKeys.Count == 0)
        {
            return [];
        }

        var resolved = new List<ResolvedAssertionKey>(assertionKeys.Count);
        foreach (var key in assertionKeys)
        {
            var seed = await RevealAsync(key.Key, secretResolver, cancellationToken);
            resolved.Add(new ResolvedAssertionKey(key.Kid, seed, key.Active));
        }

        return resolved;
    }

    private static async Task<string> RevealAsync(
        Contracts.Primitives.SecretRef secretRef, ISecretResolver secretResolver, CancellationToken cancellationToken)
    {
        using var resolved = await secretResolver.ResolveAsync(secretRef, cancellationToken);
        return resolved.Reveal();
    }
}
