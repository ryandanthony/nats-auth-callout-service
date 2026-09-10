// -----------------------------------------------------------------------
// <copyright file="ResolvedBootstrapSecrets.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Host.Wiring;

/// <summary>The plaintext secrets resolved from the bootstrap layer at startup (doc 08 task 1).</summary>
/// <param name="IssuerSeed">Signs minted user JWTs and the authorization response (doc 05).</param>
/// <param name="ServiceUserSeed">Authenticates the callout's own NATS connection, or null when unconfigured.</param>
/// <param name="XkeySeed">Decrypts/encrypts xkey-sealed callout traffic, or null when unconfigured (ADR 0018).</param>
public sealed record ResolvedBootstrapSecrets(string IssuerSeed, string? ServiceUserSeed, string? XkeySeed);
