// -----------------------------------------------------------------------
// <copyright file="HostPorts.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Host.Wiring;

/// <summary>Pinned network ports (D21).</summary>
public static class HostPorts
{
    /// <summary>Gets the pinned health/readiness/startup probe port (D21).</summary>
    /// <value>The health port, <c>8080</c>.</value>
#pragma warning disable S109 // This is the named constant itself (D21's pinned port); nothing to extract it from.
    public static int Health => 8080;
#pragma warning restore S109
}
