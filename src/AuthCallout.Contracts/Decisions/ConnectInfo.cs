// -----------------------------------------------------------------------
// <copyright file="ConnectInfo.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Decisions;

/// <summary>
/// Transport-decoupled connection metadata from the NATS authorization request's
/// <c>client_info</c>/<c>connect_opts</c> (doc 03: "connect info"), carried through for
/// logging, metrics, and event payloads. None of these fields drive resolution logic
/// (doc 03's stages key only on the token and validated claims) — this is why they live in
/// one small, optional-everywhere record rather than individual pipeline parameters. P07's
/// <c>CalloutResponder</c> populates this from the decoded <c>NATS.Jwt</c> authorization
/// request; <c>AuthCallout.Core</c> never references <c>NATS.Jwt</c> types directly.
/// </summary>
/// <param name="Host">The connecting client's remote host/IP, if known.</param>
/// <param name="Name">The client-supplied connection name (CONNECT <c>name</c>), if any.</param>
/// <param name="ClientLanguage">The client library's advertised language (CONNECT <c>lang</c>), if any.</param>
/// <param name="ClientVersion">The client library's advertised version (CONNECT <c>version</c>), if any.</param>
public sealed record ConnectInfo(string? Host, string? Name, string? ClientLanguage, string? ClientVersion)
{
    /// <summary>Gets an empty connect-info instance, for callers (e.g. tests) with nothing to report.</summary>
    /// <value>An empty connect-info instance.</value>
    public static ConnectInfo Empty { get; } = new(Host: null, Name: null, ClientLanguage: null, ClientVersion: null);
}
