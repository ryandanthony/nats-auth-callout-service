// -----------------------------------------------------------------------
// <copyright file="CalloutConnectionInfo.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NATS.Client.Core;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>The OIDC token and connection options produced by <see cref="CalloutConnection.PrepareAsync"/>.</summary>
/// <param name="Token">The raw OIDC access token (the client's bearer credential; also its own <c>Callout-Identity</c> lookup key).</param>
/// <param name="Options">The NATS connection options (URL + token + scoped inbox prefix).</param>
public sealed record CalloutConnectionInfo(string Token, NatsOpts Options);
