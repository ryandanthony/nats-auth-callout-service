// -----------------------------------------------------------------------
// <copyright file="AnonymousPing.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Wolverine;

namespace AuthCallout.Integration.Scenarios;

/// <summary>A message type opted out of <see cref="CalloutIdentityMiddleware"/> (scenario 3): the handler must run with no identity header present.</summary>
/// <param name="Payload">An arbitrary payload, recorded by the handler so the test can assert it ran.</param>
[RequireIdentity(false)]
public sealed record AnonymousPing(string Payload);
