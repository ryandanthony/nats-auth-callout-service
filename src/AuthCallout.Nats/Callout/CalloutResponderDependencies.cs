// -----------------------------------------------------------------------
// <copyright file="CalloutResponderDependencies.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;

namespace AuthCallout.Nats.Callout;

/// <summary>
/// The collaborators <see cref="CalloutResponder"/> needs, grouped into one type so its
/// constructor stays within the repo's parameter-count analyzer limit (each collaborator is
/// still a small, independently-substitutable Contracts interface; this is purely a
/// parameter-list ergonomics grouping, not a new abstraction).
/// </summary>
/// <param name="Pipeline">The resolution pipeline (P05) to decide each request.</param>
/// <param name="ActiveConfigSource">The current config snapshot, read once per request (per-request consistency, doc 08 §Config reload).</param>
/// <param name="Minter">Mints and signs the authorization response (P07).</param>
/// <param name="Crypto">Xkey seal/open, real or no-op (ADR 0018, P07).</param>
/// <param name="IdentityStoreWriter">Writes identity before the reply on a propagation-enabled grant (P08).</param>
/// <param name="EventEmitter">Emits the outcome event after the reply, fire-and-forget (P09).</param>
public sealed record CalloutResponderDependencies(
    IResolutionPipeline Pipeline,
    IActiveConfigSource ActiveConfigSource,
    IUserJwtMinter Minter,
    ICalloutCrypto Crypto,
    IIdentityStoreWriter IdentityStoreWriter,
    IEventEmitter EventEmitter)
{
    /// <summary>Validates that every collaborator is non-null, throwing <see cref="ArgumentNullException"/> otherwise.</summary>
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Pipeline);
        ArgumentNullException.ThrowIfNull(ActiveConfigSource);
        ArgumentNullException.ThrowIfNull(Minter);
        ArgumentNullException.ThrowIfNull(Crypto);
        ArgumentNullException.ThrowIfNull(IdentityStoreWriter);
        ArgumentNullException.ThrowIfNull(EventEmitter);
    }
}
