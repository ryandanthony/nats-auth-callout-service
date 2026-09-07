// -----------------------------------------------------------------------
// <copyright file="SignedAuthResponse.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// A signed NATS authorization response, ready to reply on the callout subject (doc 03
/// §Mint and respond). Wraps either a minted user JWT (grant) or an authorization error
/// (denial); the byte payload is what the transport (P07) sends back as-is.
/// </summary>
/// <param name="EncodedResponseJwt">The signed authorization response JWT, encoded exactly as the NATS server expects on <c>$SYS.REQ.USER.AUTH</c>'s reply.</param>
public sealed record SignedAuthResponse(string EncodedResponseJwt);
