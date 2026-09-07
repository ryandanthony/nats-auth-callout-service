// -----------------------------------------------------------------------
// <copyright file="ServiceState.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// The service's top-level lifecycle state (doc 05 §Service state machine). Exactly one is
/// current at a time; transitions follow doc 05's state diagram verbatim (e.g. <c>Idle</c>
/// never holds a NATS connection at all; <c>Serving</c> requires a valid config, resolved
/// secrets, JWKS prewarm, and an active, subscribed NATS connection).
/// </summary>
public enum ServiceState
{
    /// <summary>Loading bootstrap config, resolving secrets, selecting the routing source.</summary>
    Starting,

    /// <summary>No valid routing config. The service holds no NATS connection at all and answers nothing.</summary>
    Idle,

    /// <summary>Holds the NATS connection, subscribed to <c>$SYS.REQ.USER.AUTH</c>, answering requests using the current config.</summary>
    Serving,

    /// <summary>Graceful shutdown in progress (SIGINT/SIGTERM received).</summary>
    Stopping,
}
