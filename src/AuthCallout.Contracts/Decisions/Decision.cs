// -----------------------------------------------------------------------
// <copyright file="Decision.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Decisions;

/// <summary>
/// The outcome of resolving one <see cref="AuthRequest"/> against an
/// <see cref="Config.ActiveConfig"/> (<see cref="Seams.IResolutionPipeline"/>, doc 03).
/// A discriminated union of exactly <see cref="GrantedDecision"/> and
/// <see cref="DeniedDecision"/> (discriminated-union style, QUESTIONS/packet wording):
/// pattern-match on the concrete type (<c>decision switch { GrantedDecision g => ..., DeniedDecision d => ... }</c>)
/// rather than an outcome enum, so the compiler enforces exhaustiveness (a
/// non-exhaustive switch expression is a warning-as-error under this repo's analyzers) and
/// each case's fields are only ever present on the case that has them. Carries every field
/// the downstream stages (P07 minter/responder, P08 identity store writer, P09 event
/// emitter) need, so none of them re-derive resolution state.
/// </summary>
/// <param name="Application">The name of the application selected during resolution, or null when no application was selected (anonymous, or denied before app selection).</param>
/// <param name="Principal">The <c>sub</c>/<c>azp</c>-style principal identifier for logging/events (ADR 0008: "never the raw token"), or null when unavailable (e.g. denied before validation).</param>
/// <param name="Issuer">The token's original OIDC issuer authority, or null when unavailable (anonymous, or denied before an issuer was read).</param>
public abstract record Decision(string? Application, string? Principal, string? Issuer)
{
    private protected Decision()
        : this(Application: null, Principal: null, Issuer: null)
    {
    }
}
