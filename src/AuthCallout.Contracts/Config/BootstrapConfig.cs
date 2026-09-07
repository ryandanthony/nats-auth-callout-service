// -----------------------------------------------------------------------
// <copyright file="BootstrapConfig.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Internal;
using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// The bootstrap layer: how to connect and where to read routing config from, loaded once
/// at startup and never hot-reloaded (doc 04 §Two layers, ADR 0005, ADR 0007).
/// </summary>
/// <param name="NatsUrl">The NATS server URL the service connects to.</param>
/// <param name="IssuerSeed">Signs minted user JWTs and the authorization response.</param>
/// <param name="ServiceUserSeed">The callout's own NATS user seed, authenticating its connection. Optional (e.g. some deployments authenticate the service connection another way).</param>
/// <param name="XkeySeed">
/// The xkey curve seed used to decrypt incoming <c>$SYS.REQ.USER.AUTH</c> requests and
/// encrypt responses (ADR 0018, D14/QUESTIONS D14). Optional; when null, the callout
/// exchanges plaintext requests/responses and an encrypted request received while
/// unconfigured is rejected (fail closed, P07). All instances sharing a queue group must
/// use the same seed (doc 08 §Multiple instances).
/// </param>
/// <param name="AssertionKeys">The assertion signing keyset (ADR 0011, ADR 0014). Null/empty when identity-propagation assertions are not signed by any application (no application needs propagation).</param>
/// <param name="Source">Which routing/apps provider to use and its settings, plus shared watch-health thresholds.</param>
/// <param name="Jwks">JWKS cache manager settings.</param>
/// <param name="Concurrency">In-process parallelism and backpressure settings.</param>
/// <param name="Anonymous">The global anonymous-grant configuration.</param>
public sealed record BootstrapConfig(
    string NatsUrl,
    SecretRef IssuerSeed,
    SecretRef? ServiceUserSeed,
    SecretRef? XkeySeed,
    IReadOnlyList<AssertionKey>? AssertionKeys,
    SourceConfig Source,
    JwksSettings Jwks,
    ConcurrencySettings Concurrency,
    AnonymousGlobalConfig Anonymous)
{
    /// <inheritdoc />
    public bool Equals(BootstrapConfig? other)
    {
        if (other is null)
        {
            return false;
        }

        var coreEqual = string.Equals(NatsUrl, other.NatsUrl, StringComparison.Ordinal)
            && IssuerSeed.Equals(other.IssuerSeed)
            && Equals(ServiceUserSeed, other.ServiceUserSeed)
            && Equals(XkeySeed, other.XkeySeed);
        var settingsEqual = Source.Equals(other.Source)
            && Jwks.Equals(other.Jwks)
            && Concurrency.Equals(other.Concurrency)
            && Anonymous.Equals(other.Anonymous);
        return coreEqual && settingsEqual && StructuralEquality.SequenceEqual(AssertionKeys, other.AssertionKeys);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(NatsUrl);
        hash.Add(IssuerSeed);
        hash.Add(ServiceUserSeed);
        hash.Add(XkeySeed);
        hash.Add(Source);
        hash.Add(Jwks);
        hash.Add(Concurrency);
        hash.Add(Anonymous);
        hash.Add(StructuralEquality.SequenceHashCode(AssertionKeys));
        return hash.ToHashCode();
    }
}
