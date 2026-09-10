// -----------------------------------------------------------------------
// <copyright file="IEventEmitter.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Decisions;

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// Publishes best-effort auth outcome events (ADR 0008, doc 09 §1). Implemented by
/// <c>AuthCallout.Nats</c> (P09); consumed by the callout responder (P07) off the critical
/// path (fire-and-forget after the reply is sent) and, for anonymous outcomes, by
/// <c>AuthCallout.Host</c> where applicable.
/// </summary>
public interface IEventEmitter
{
    /// <summary>
    /// Emits the outcome event for a decision, if a subject is configured for it (doc 05:
    /// per-app success/failure subjects, or the global anonymous subjects; no subject
    /// configured means no event, silently). Never throws: a publish failure is caught,
    /// logged, and metered internally rather than propagated to the caller, and never
    /// delays or blocks the caller (ADR 0008: "must never delay or block the auth
    /// response").
    /// </summary>
    /// <param name="decision">The resolution outcome to emit an event for.</param>
    /// <param name="userNKey">
    /// The connecting user's NKey from the originating <see cref="AuthRequest"/> (ADR 0008,
    /// doc 09 optional <c>userNKey</c> payload field). The caller (P07 responder / Host)
    /// supplies it because <see cref="Decision"/> is transport-agnostic and does not carry
    /// it; <see langword="null"/> when unavailable, in which case the field is omitted.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the publish attempt.</param>
    /// <returns>A task that completes when the emit attempt (success or swallowed failure) is done.</returns>
    Task EmitAsync(Decision decision, string? userNKey, CancellationToken cancellationToken);
}
