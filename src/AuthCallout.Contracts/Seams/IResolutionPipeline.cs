// -----------------------------------------------------------------------
// <copyright file="IResolutionPipeline.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// The fixed issuer → validate → app → profile → grant resolution pipeline (doc 03, ADR
/// 0003). Implemented by <c>AuthCallout.Core</c> (P05); consumed by the callout responder
/// (P07) and, for manual smoke checks, <c>AuthCallout.Host</c> (P13). Pure decision logic:
/// no I/O beyond delegating token validation to <see cref="ITokenValidator"/>; storage and
/// event emission happen outside this call, orchestrated by the caller around the returned
/// <see cref="Decision"/> (doc 03 §Store identity / §Outcome events).
/// </summary>
public interface IResolutionPipeline
{
    /// <summary>
    /// Resolves one <see cref="AuthRequest"/> against a config snapshot, following doc 03's
    /// fixed stage order exactly: no token → anonymous composer; else issuer lookup →
    /// token validation → application <c>Match</c> → profile <c>Match</c> → grant
    /// construction. Stateless and thread-safe per call over the immutable
    /// <paramref name="config"/> snapshot (doc 08 §In-process parallelism prerequisite): safe
    /// to invoke concurrently from many <c>Parallel.ForEachAsync</c> workers.
    /// </summary>
    /// <param name="request">The decoded authorization request.</param>
    /// <param name="config">The active config snapshot to resolve against.</param>
    /// <param name="cancellationToken">Cancellation, bounded by the per-request timeout.</param>
    /// <returns>The resolved decision: a grant or a denial.</returns>
    Task<Decision> ResolveAsync(AuthRequest request, ActiveConfig config, CancellationToken cancellationToken);
}
