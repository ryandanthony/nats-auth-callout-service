// -----------------------------------------------------------------------
// <copyright file="AuthRequestDecoder.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using AuthCallout.Contracts.Decisions;
using NATS.Jwt;
using NATS.Jwt.Models;

namespace AuthCallout.Nats.Callout;

/// <summary>
/// Decodes a plaintext (already-decrypted, if applicable, ADR 0018) <c>$SYS.REQ.USER.AUTH</c>
/// payload into the transport-decoupled <see cref="AuthRequest"/> (doc 03 §Stages: "user
/// NKey, token, connect info, server id"). Split out of <see cref="CalloutResponder"/> to keep
/// the responder's own type-coupling within the repo's complexity analyzer limits and to make
/// decode failure handling independently testable.
/// </summary>
public static class AuthRequestDecoder
{
    /// <summary>
    /// Decodes <paramref name="payloadBytes"/> as a <c>NATS.Jwt</c> authorization request and
    /// maps it into an <see cref="AuthRequest"/>.
    /// </summary>
    /// <param name="payloadBytes">The plaintext (post-decryption) request payload.</param>
    /// <returns>The mapped, transport-decoupled request.</returns>
    /// <exception cref="FormatException">The payload is missing a required field.</exception>
    /// <exception cref="NatsJwtException">The payload is not a well-formed, self-consistent NATS JWT.</exception>
    public static AuthRequest Decode(byte[] payloadBytes)
    {
        ArgumentNullException.ThrowIfNull(payloadBytes);
        var payloadText = Encoding.UTF8.GetString(payloadBytes);
        var claims = NatsJwt.DecodeClaims<NatsAuthorizationRequestClaims>(payloadText);
        return Map(claims);
    }

    private static AuthRequest Map(NatsAuthorizationRequestClaims claims)
    {
        var authRequest = claims.AuthorizationRequest ?? throw new FormatException("Auth request claims carry no 'nats' authorization-request body.");
        if (string.IsNullOrEmpty(authRequest.UserNKey))
        {
            throw new FormatException("Auth request is missing 'user_nkey'.");
        }

        var serverId = authRequest.NatsServer?.Id;
        if (string.IsNullOrEmpty(serverId))
        {
            throw new FormatException("Auth request is missing the server id.");
        }

        var connectOpts = authRequest.NatsConnectOptions;
        var clientInfo = authRequest.NatsClientInformation;
        var rawToken = string.IsNullOrEmpty(connectOpts?.Token) ? null : connectOpts.Token;
        var connectInfo = new ConnectInfo(
            Host: clientInfo?.Host,
            Name: connectOpts?.Name,
            ClientLanguage: connectOpts?.Lang,
            ClientVersion: connectOpts?.Version);

        return new AuthRequest(authRequest.UserNKey, rawToken, connectInfo, serverId);
    }
}
