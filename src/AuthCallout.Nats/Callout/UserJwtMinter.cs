// -----------------------------------------------------------------------
// <copyright file="UserJwtMinter.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;
using NATS.Jwt;
using NATS.Jwt.Models;
using NATS.NKeys;

namespace AuthCallout.Nats.Callout;

/// <summary>
/// <see cref="IUserJwtMinter"/>: mints the minimal NATS user JWT for a grant (D18: account,
/// name, expiry, and publish/subscribe allow lists only — no bearer flag, connection-type
/// limits, or response permissions) or a generic authorization-error response for a denial,
/// both wrapped in a signed authorization response bound to the requesting user NKey and
/// server id (doc 03 §Mint and respond). Both the user JWT and the outer authorization
/// response are signed with the same **issuer signing seed** (doc 05 §Signing and connection
/// identity, doc 08: "The minter signs both the user JWT and the authorization response with
/// the issuer signing seed").
/// </summary>
public sealed class UserJwtMinter : IUserJwtMinter, IDisposable
{
    /// <summary>
    /// The generic denial error string returned to the client (doc 03: "no internal detail
    /// leak"). The actual <see cref="DeniedDecision.Reason"/>/<see cref="DeniedDecision.Detail"/>
    /// never reach this string — they go to events/logs only (P09/host logging).
    /// </summary>
#pragma warning disable S2339 // Kept const (matching the repo's existing named-constant pattern, e.g. PostgresSourceSettings.DefaultNotifyChannel), not a mutable-state concern.
    public const string GenericDenialError = "authorization denied";
#pragma warning restore S2339

    private readonly KeyPair _issuerKeyPair;

    /// <summary>Initializes a new instance of the <see cref="UserJwtMinter"/> class.</summary>
    /// <param name="issuerSeed">The resolved Account-prefixed NKey seed that signs every minted user JWT and authorization response (doc 05).</param>
    public UserJwtMinter(string issuerSeed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerSeed);
        _issuerKeyPair = KeyPair.FromSeed(issuerSeed);
    }

    /// <inheritdoc />
    public SignedAuthResponse MintGrant(GrantedDecision grant, string userNKey, string serverId)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentException.ThrowIfNullOrWhiteSpace(userNKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);

        var userClaims = NatsJwt.NewUserClaims(userNKey);
        userClaims.User.IssuerAccount = _issuerKeyPair.GetPublicKey();
        userClaims.Audience = grant.TargetAccount;
        userClaims.Name = grant.Name;
        userClaims.Expires = grant.Expiry;
        userClaims.User.Pub = new NatsPermission { Allow = [.. grant.Publish] };
        userClaims.User.Sub = new NatsPermission { Allow = [.. grant.Subscribe] };
        var userJwt = NatsJwt.EncodeUserClaims(userClaims, _issuerKeyPair);

        return EncodeResponse(userNKey, serverId, userJwt, error: null);
    }

    /// <inheritdoc />
    public SignedAuthResponse MintDenial(DeniedDecision denial, string userNKey, string serverId)
    {
        ArgumentNullException.ThrowIfNull(denial);
        ArgumentException.ThrowIfNullOrWhiteSpace(userNKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);

        return EncodeResponse(userNKey, serverId, userJwt: null, GenericDenialError);
    }

    /// <summary>Disposes the underlying issuer key pair.</summary>
    public void Dispose() => _issuerKeyPair.Dispose();

    private SignedAuthResponse EncodeResponse(string userNKey, string serverId, string? userJwt, string? error)
    {
        var responseClaims = NatsJwt.NewAuthorizationResponseClaims(userNKey);
        responseClaims.Audience = serverId;
        if (userJwt is not null)
        {
            responseClaims.AuthorizationResponse.Jwt = userJwt;
        }

        if (error is not null)
        {
            responseClaims.AuthorizationResponse.Error = error;
        }

        var responseJwt = NatsJwt.EncodeAuthorizationResponseClaims(responseClaims, _issuerKeyPair);
        return new SignedAuthResponse(responseJwt);
    }
}
