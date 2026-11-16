// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityMiddleware.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Identity;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// Inbound half of ADR 0022 task 3: promoted from <c>dev/samples/Shared/CalloutIdentityMiddleware.cs</c>
/// to the package, now built on <see cref="ICalloutIdentityVerifierAccessor"/> rather than a
/// directly-injected verifier, since this host's verifier is only available once Wolverine's
/// transport has connected (its KV reads and discovery fetch run over that connection, see
/// <see cref="CalloutTransportContext"/>), which the DI container cannot wait for synchronously.
/// <see cref="CalloutIdentityPolicy"/> binds
/// this middleware to every handler chain by default (packet P20's "policy so apps can't forget
/// a handler").
/// </summary>
public static class CalloutIdentityMiddleware
{
    /// <summary>
    /// Verifies the inbound envelope's identity assertion before the handler runs.
    /// </summary>
    /// <param name="envelope">The inbound Wolverine envelope (carries the NATS headers, including <c>Callout-Identity</c>).</param>
    /// <param name="verifierAccessor">The per-host verifier accessor, registered by <see cref="NatsTransportExtensions.UseNatsWithCallout"/>.</param>
    /// <param name="logger">The handler's logger (Wolverine injects <c>ILogger&lt;messageType&gt;</c>).</param>
    /// <returns>
    /// A tuple of the continuation and the verified <see cref="CalloutIdentityContext"/>:
    /// <see cref="HandlerContinuation.Continue"/> with a non-null context on success, or
    /// <see cref="HandlerContinuation.Stop"/> with null when the identity is missing, forged, or expired.
    /// </returns>
    public static async Task<(HandlerContinuation continuation, CalloutIdentityContext? identity)> LoadAsync(
        Envelope envelope, ICalloutIdentityVerifierAccessor verifierAccessor, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(verifierAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        if (!envelope.Headers.TryGetValue(CalloutIdentityHeader.FallbackName, out var assertion) || string.IsNullOrEmpty(assertion))
        {
            logger.LogWarning("Rejecting {MessageType}: no {Header} identity header present.", envelope.MessageType, CalloutIdentityHeader.FallbackName);
            return (HandlerContinuation.Stop, null);
        }

        var verifier = await verifierAccessor.GetVerifierAsync().ConfigureAwait(false);
        var claims = await verifier.VerifyAsync(assertion).ConfigureAwait(false);
        if (claims is null)
        {
            logger.LogWarning("Rejecting {MessageType}: {Header} assertion failed verification (bad signature/expired/unknown kid).", envelope.MessageType, CalloutIdentityHeader.FallbackName);
            return (HandlerContinuation.Stop, null);
        }

        logger.LogInformation("Verified caller identity for {MessageType}: sub={Subject} app={Application}.", envelope.MessageType, claims.Subject, claims.Application);
        return (HandlerContinuation.Continue, new CalloutIdentityContext(claims));
    }
}
