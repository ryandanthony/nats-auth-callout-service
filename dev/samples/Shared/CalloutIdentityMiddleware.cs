// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityMiddleware.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Wolverine;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// Wolverine message-handler middleware that enforces callout identity propagation on the
/// service side (doc 06 "How the subscriber resolves the caller's identity", ADR 0011): it
/// reads the <c>Callout-Identity</c> header off the inbound message envelope, verifies the
/// signed assertion (signature against the keys bucket + expiry) via
/// <see cref="CalloutIdentityResolver"/>, and either
/// <list type="bullet">
/// <item><description>returns <see cref="HandlerContinuation.Continue"/> plus a populated
/// <see cref="CalloutIdentityContext"/> — which Wolverine passes to the handler as a
/// method argument — when the caller's identity verifies; or</description></item>
/// <item><description>returns <see cref="HandlerContinuation.Stop"/> (with a null context)
/// when the header is absent or fails verification, so the handler never runs on an
/// unauthenticated or forged message.</description></item>
/// </list>
/// This is the "the service checks the auth of the clients" step: the callout authenticated
/// the NATS <i>connection</i>, and this middleware additionally verifies the per-message
/// caller identity the client attached, on every command/query the service handles.
/// </summary>
public static class CalloutIdentityMiddleware
{
    /// <summary>
    /// Verifies the inbound message's identity assertion before the handler runs.
    /// </summary>
    /// <param name="message">The inbound client request (first parameter so Wolverine binds this middleware to <see cref="IClientRequest"/> handler chains).</param>
    /// <param name="envelope">The inbound Wolverine envelope (carries the NATS headers, including <c>Callout-Identity</c>).</param>
    /// <param name="verifier">The per-app identity verifier (registered in DI, wraps <see cref="CalloutIdentityResolver"/> against this app's discovery descriptor).</param>
    /// <param name="logger">The handler's logger (Wolverine injects <c>ILogger&lt;messageType&gt;</c>).</param>
    /// <returns>
    /// A tuple of the continuation and the verified <see cref="CalloutIdentityContext"/>:
    /// <see cref="HandlerContinuation.Continue"/> with a non-null context on success, or
    /// <see cref="HandlerContinuation.Stop"/> with null when the identity is missing or invalid.
    /// </returns>
    public static async Task<(HandlerContinuation continuation, CalloutIdentityContext? identity)> LoadAsync(
        IClientRequest message, Envelope envelope, ICalloutIdentityVerifier verifier, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(logger);

        if (!envelope.Headers.TryGetValue(CalloutIdentityHeader.FallbackName, out var assertion) || string.IsNullOrEmpty(assertion))
        {
            logger.LogWarning("Rejecting {MessageType}: no {Header} identity header present.", envelope.MessageType, CalloutIdentityHeader.FallbackName);
            return (HandlerContinuation.Stop, null);
        }

        var claims = await verifier.VerifyAsync(assertion);
        if (claims is null)
        {
            logger.LogWarning("Rejecting {MessageType}: {Header} assertion failed verification (bad signature/expired/unknown kid).", envelope.MessageType, CalloutIdentityHeader.FallbackName);
            return (HandlerContinuation.Stop, null);
        }

        logger.LogInformation("Verified caller identity for {MessageType}: sub={Subject} app={Application}.", envelope.MessageType, claims.Subject, claims.Application);
        return (HandlerContinuation.Continue, new CalloutIdentityContext(claims));
    }
}
