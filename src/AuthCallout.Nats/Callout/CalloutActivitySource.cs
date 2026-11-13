// -----------------------------------------------------------------------
// <copyright file="CalloutActivitySource.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;

namespace AuthCallout.Nats.Callout;

/// <summary>
/// The <see cref="ActivitySource"/> for the per-auth-request trace (doc 05 §Observability: "one
/// span per auth request"): a root <see cref="RequestActivityName"/> span started by
/// <see cref="CalloutResponder"/> for every message, with child spans for each stage that can
/// take real time (resolution, the identity-store writes, mint and reply). Lives in
/// <c>AuthCallout.Nats</c> rather than the Host so the responder and the KV writer can start
/// spans without a Host reference; the Host registers <see cref="Name"/> with its
/// <c>TracerProvider</c>. With no listener attached, <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
/// returns null and costs effectively nothing. Spans carry no token, claim, or secret values
/// (ADR 0008), only stage names and the grant/deny outcome.
/// </summary>
public static class CalloutActivitySource
{
    /// <summary>Gets the activity source name the Host adds to its OTel <c>TracerProvider</c>.</summary>
    /// <value>The activity source name.</value>
    public static string Name => "AuthCallout.Nats.Callout";

    /// <summary>Gets the root span name for one auth-callout request.</summary>
    /// <value>The root span name.</value>
    public static string RequestActivityName => "auth.request";

    /// <summary>Gets the shared activity source.</summary>
    /// <value>The shared activity source.</value>
    public static ActivitySource Instance { get; } = new(Name);

    /// <summary>Starts the root span for one auth-callout request (null when nothing is listening).</summary>
    /// <returns>The started span, to dispose when the request finishes; null when tracing is off.</returns>
    internal static Activity? StartRequest() => Instance.StartActivity(RequestActivityName, ActivityKind.Server);

    /// <summary>Starts a child span for one request stage (null when nothing is listening).</summary>
    /// <param name="stageName">The stage span name, e.g. <c>auth.resolve</c>.</param>
    /// <returns>The started span, to dispose when the stage finishes; null when tracing is off.</returns>
    internal static Activity? StartStage(string stageName) => Instance.StartActivity(stageName);

    /// <summary>
    /// Starts a child span for one request stage under an explicit parent rather than the
    /// ambient <see cref="Activity.Current"/>. Needed when two stages run concurrently
    /// (<c>Task.WhenAll</c>): starting the second span while the first is still current would
    /// ambiently nest it under the first (sibling stages, not parent/child) instead of under
    /// their shared parent, because <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
    /// always parents to whatever is current at the moment it is called.
    /// </summary>
    /// <param name="stageName">The stage span name.</param>
    /// <param name="parentContext">The parent span's context, captured before starting any concurrent children.</param>
    /// <returns>The started span, to dispose when the stage finishes; null when tracing is off.</returns>
    internal static Activity? StartStage(string stageName, ActivityContext parentContext) =>
        Instance.StartActivity(stageName, ActivityKind.Internal, parentContext);

    /// <summary>Runs <paramref name="stage"/> inside a child span named <paramref name="stageName"/>.</summary>
    /// <typeparam name="T">The stage result type.</typeparam>
    /// <param name="stageName">The stage span name.</param>
    /// <param name="stage">The stage body.</param>
    /// <returns>The stage result.</returns>
    internal static async Task<T> TraceStageAsync<T>(string stageName, Func<Task<T>> stage)
    {
        using var span = StartStage(stageName);
        return await stage();
    }

    /// <summary>Runs <paramref name="stage"/> inside a child span named <paramref name="stageName"/>.</summary>
    /// <param name="stageName">The stage span name.</param>
    /// <param name="stage">The stage body.</param>
    /// <returns>A task that completes when the stage does.</returns>
    internal static async Task TraceStageAsync(string stageName, Func<Task> stage)
    {
        using var span = StartStage(stageName);
        await stage();
    }

    /// <summary>Runs <paramref name="stage"/> inside a child span under an explicit parent context (see <see cref="StartStage(string, ActivityContext)"/>). For stages run concurrently with a sibling.</summary>
    /// <param name="stageName">The stage span name.</param>
    /// <param name="parentContext">The parent span's context.</param>
    /// <param name="stage">The stage body.</param>
    /// <returns>A task that completes when the stage does.</returns>
    internal static async Task TraceStageAsync(string stageName, ActivityContext parentContext, Func<Task> stage)
    {
        using var span = StartStage(stageName, parentContext);
        await stage();
    }

    /// <summary>Tags the current span with the request outcome (<c>granted</c>/<c>denied</c>).</summary>
    /// <param name="granted">Whether the request was granted.</param>
    internal static void TagOutcome(bool granted) => Activity.Current?.SetTag("auth.outcome", granted ? "granted" : "denied");
}
