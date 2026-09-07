// -----------------------------------------------------------------------
// <copyright file="AuthRequest.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Decisions;

/// <summary>
/// One decoded <c>$SYS.REQ.USER.AUTH</c> authorization request (doc 03 §Stages: "user NKey,
/// token, connect info, server id"), decoupled from <c>NATS.Jwt</c> types so
/// <c>AuthCallout.Core</c> stays transport-free. <see cref="RawToken"/> is null exactly
/// when the connection is anonymous (doc 03 Stage 0); every non-null token is subject to
/// the full issuer → validate → app → profile pipeline (ADR 0003).
/// </summary>
/// <param name="UserNKey">The one-time NKey the server generated for this connection attempt; the authorization response is signed for this subject (doc 03 §Mint and respond).</param>
/// <param name="RawToken">The client's presented bearer token (raw, undecoded JWT text), or null for an anonymous connection.</param>
/// <param name="ConnectInfo">Transport-decoupled connection metadata, for logging/events only.</param>
/// <param name="ServerId">The requesting NATS server's id; the authorization response's audience (doc 03 §Mint and respond).</param>
public sealed record AuthRequest(string UserNKey, string? RawToken, ConnectInfo ConnectInfo, string ServerId)
{
    /// <summary>Gets a value indicating whether no token was presented (the anonymous path, doc 03 Stage 0).</summary>
    /// <value>True when no token was presented.</value>
    public bool IsAnonymous => RawToken is null;
}
