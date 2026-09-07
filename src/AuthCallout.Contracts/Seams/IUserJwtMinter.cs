// -----------------------------------------------------------------------
// <copyright file="IUserJwtMinter.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Decisions;

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// Mints the minimal NATS user JWT for a grant, or an authorization-error response for a
/// denial (D18: account/name/exp/pub-sub allow lists only — no bearer flag, connection-type
/// limits, or response permissions), both wrapped in a signed authorization response bound
/// to the requesting user NKey and server id (doc 03 §Mint and respond). Implemented by
/// <c>AuthCallout.Nats</c> (P07) using <c>NATS.Jwt</c>/<c>NATS.NKeys</c>; consumed by the
/// callout responder (P07).
/// </summary>
public interface IUserJwtMinter
{
    /// <summary>
    /// Mints a signed authorization response granting the connection the decision's
    /// account, name, expiry, and publish/subscribe allow lists.
    /// </summary>
    /// <param name="grant">The successful resolution to mint.</param>
    /// <param name="userNKey">The request's one-time user NKey (the response's subject).</param>
    /// <param name="serverId">The requesting server's id (the response's audience).</param>
    /// <returns>The signed authorization response carrying the minted user JWT.</returns>
    SignedAuthResponse MintGrant(GrantedDecision grant, string userNKey, string serverId);

    /// <summary>
    /// Mints a signed authorization response denying the connection. The response carries a
    /// generic error string only — <paramref name="denial"/>'s <see cref="DeniedDecision.Reason"/>
    /// and <see cref="DeniedDecision.Detail"/> never reach the client (doc 03: internal detail
    /// goes to events/logs only).
    /// </summary>
    /// <param name="denial">The denial to mint a response for.</param>
    /// <param name="userNKey">The request's one-time user NKey (the response's subject).</param>
    /// <param name="serverId">The requesting server's id (the response's audience).</param>
    /// <returns>The signed authorization response carrying a generic denial.</returns>
    SignedAuthResponse MintDenial(DeniedDecision denial, string userNKey, string serverId);
}
