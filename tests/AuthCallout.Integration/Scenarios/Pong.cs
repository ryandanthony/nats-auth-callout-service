// -----------------------------------------------------------------------
// <copyright file="Pong.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Integration.Scenarios;

/// <summary>The reply <see cref="PingHandlers"/> sends back, carrying the verified caller's <c>sub</c> as seen by the service.</summary>
/// <param name="Payload">Echoes <see cref="Ping.Payload"/>.</param>
/// <param name="VerifiedSubject">The <see cref="AuthCallout.Client.Identity.CalloutIdentityContext.Subject"/> the service's inbound middleware verified for the <see cref="Ping"/>.</param>
public sealed record Pong(string Payload, string VerifiedSubject);
