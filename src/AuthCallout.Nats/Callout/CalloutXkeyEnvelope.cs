// -----------------------------------------------------------------------
// <copyright file="CalloutXkeyEnvelope.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using NATS.Client.Core;

namespace AuthCallout.Nats.Callout;

/// <summary>
/// Detects and applies xkey encryption/decryption for one <c>$SYS.REQ.USER.AUTH</c> request
/// (ADR 0018): whether the request is encrypted is read from the
/// <see cref="CalloutResponder.XkeyHeaderName"/> message header, never guessed from payload
/// content. Split out of <see cref="CalloutResponder"/> to keep the responder's own
/// type-coupling within the repo's complexity analyzer limits.
/// </summary>
public static class CalloutXkeyEnvelope
{
    /// <summary>
    /// Reads the request's xkey envelope: whether it was encrypted (the
    /// <see cref="CalloutResponder.XkeyHeaderName"/> header's presence, per ADR 0018 — never
    /// content-sniffed) and, if so, opens it against <paramref name="crypto"/>.
    /// </summary>
    /// <param name="msg">The received message.</param>
    /// <param name="crypto">The configured (or no-op) callout crypto.</param>
    /// <returns>
    /// The plaintext payload and the server's ephemeral xkey public (null when the request
    /// was plaintext), or a rejected result when the request must be dropped (an
    /// encrypted-while-unconfigured or plaintext-while-configured mismatch, ADR 0018 fail
    /// closed).
    /// </returns>
    public static CalloutXkeyOpenResult Open(NatsMsg<byte[]> msg, ICalloutCrypto crypto)
    {
        ArgumentNullException.ThrowIfNull(crypto);
        var serverXkeyPublic = TryGetServerXkeyHeader(msg);
        if (serverXkeyPublic is not null)
        {
            if (!crypto.IsEnabled)
            {
                return CalloutXkeyOpenResult.Reject("Encrypted auth request received but xkey is not configured; dropped without reply (ADR 0018 fail closed).");
            }

            var opened = crypto.Open(msg.Data ?? [], serverXkeyPublic).Payload;
            return CalloutXkeyOpenResult.Accept(opened, serverXkeyPublic);
        }

        if (crypto.IsEnabled)
        {
            return CalloutXkeyOpenResult.Reject("Plaintext auth request received but xkey is configured; dropped without reply (ADR 0018 fail closed).");
        }

        return CalloutXkeyOpenResult.Accept(msg.Data ?? [], serverXkeyPublic: null);
    }

    /// <summary>Seals <paramref name="responseBytes"/> for the server's ephemeral public key when the request was encrypted; otherwise returns it unchanged.</summary>
    /// <param name="responseBytes">The plaintext signed response bytes.</param>
    /// <param name="serverXkeyPublic">The server's ephemeral public xkey from the request, or null when the request was plaintext.</param>
    /// <param name="crypto">The configured (or no-op) callout crypto.</param>
    /// <returns>The response bytes to publish: sealed when <paramref name="serverXkeyPublic"/> is set, unchanged otherwise.</returns>
    public static byte[] Seal(byte[] responseBytes, string? serverXkeyPublic, ICalloutCrypto crypto)
    {
        ArgumentNullException.ThrowIfNull(responseBytes);
        ArgumentNullException.ThrowIfNull(crypto);
        return serverXkeyPublic is null ? responseBytes : crypto.Seal(responseBytes, serverXkeyPublic).Payload;
    }

    private static string? TryGetServerXkeyHeader(NatsMsg<byte[]> msg) =>
        msg.Headers is { } headers && headers.TryGetLastValue(CalloutResponder.XkeyHeaderName, out var value) ? value : null;
}
