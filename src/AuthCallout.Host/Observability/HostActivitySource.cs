// -----------------------------------------------------------------------
// <copyright file="HostActivitySource.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;

namespace AuthCallout.Host.Observability;

/// <summary>
/// The <see cref="ActivitySource"/> for the one-span-per-auth-request trace (doc 05
/// §Observability: "one span per auth request covering the four resolution stages"; doc 08
/// §Auth callout implementation shape). Exposed as static well-known names so any component
/// package (P05/P06/P07/P08/P09) can start a child <see cref="Activity"/> under the request
/// span it is called within, without taking a project reference on <c>AuthCallout.Host</c>
/// (an <see cref="ActivitySource"/> is process-wide, keyed only by name/version). P13
/// registers <see cref="Name"/> with the OTel <c>TracerProvider</c>; starting the root span
/// itself is <c>CalloutResponder</c>'s (P07) concern per request, this type only pins the
/// shared name and stage-event constants so every component's traces line up under it.
/// </summary>
public static class HostActivitySource
{
    /// <summary>Gets the activity source name registered with the OTel <c>TracerProvider</c>.</summary>
    /// <value>The activity source name, <c>AuthCallout.Host</c>.</value>
    public static string Name => "AuthCallout.Host";

    /// <summary>Gets the shared <see cref="ActivitySource"/> instance every component's per-stage activity is started from.</summary>
    /// <value>The shared activity source.</value>
    public static ActivitySource Instance { get; } = new(Name);

    /// <summary>Gets the root activity name for one auth request (doc 03/08: issuer -&gt; validate -&gt; app -&gt; profile -&gt; grant).</summary>
    /// <value>The root activity name.</value>
    public static string AuthRequestActivityName => "auth.request";
}
