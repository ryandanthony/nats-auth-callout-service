// -----------------------------------------------------------------------
// <copyright file="CalloutConnectionState.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Client.Connection;

/// <summary>The lifecycle state of a <see cref="CalloutNatsConnection"/> (ADR 0021).</summary>
public enum CalloutConnectionState
{
    /// <summary>Not yet connected, or disposed.</summary>
    Disconnected,

    /// <summary>A connect attempt is in progress (first attempt or a self-owned reconnect).</summary>
    Connecting,

    /// <summary>Connected and usable.</summary>
    Connected,

    /// <summary>Disconnected and retrying with backoff (transport failure) or after one forced-refresh retry (auth violation).</summary>
    Retrying,

    /// <summary>Terminally failed: either the auth-violation retry was exhausted, or the token provider returned null.</summary>
    Failed,
}
