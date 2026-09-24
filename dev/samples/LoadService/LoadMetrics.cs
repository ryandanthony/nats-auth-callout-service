// -----------------------------------------------------------------------
// <copyright file="LoadMetrics.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics.Metrics;
using AuthCallout.Nats.Callout;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace AuthCallout.Dev.Samples.LoadService;

/// <summary>
/// Client-side view of each callout-driven connect, to compare against the callout's own
/// server-side <c>callout_request_duration_seconds</c>. The callout only times receive-to-reply
/// inside its handler (the "API work"); <see cref="ConnectDurationHistogramName"/> times the
/// whole <c>ConnectAsync</c> as the client experiences it (TCP connect, NATS INFO/CONNECT,
/// the server's round trip to the callout over <c>$SYS.REQ.USER.AUTH</c>, the callout's work,
/// and the server applying the reply). The gap between the two histograms is the overhead
/// outside the callout. Both use the same bucket boundaries so they line up bucket-for-bucket
/// in the Aspire dashboard.
/// </summary>
public static class LoadMetrics
{
    private const string MeterName = "AuthCallout.Dev.LoadService";

    // Client-observed connect latency, tagged outcome=connected|failed.
    private const string ConnectDurationHistogramName = "load_connect_duration_seconds";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> ConnectDuration = Meter.CreateHistogram<double>(
        ConnectDurationHistogramName,
        unit: "s",
        description: "Client-observed seconds for a full NATS connect through the auth callout (compare with callout_request_duration_seconds).");

    private static long _intervalTicks;
    private static long _intervalCount;

    /// <summary>
    /// Builds the OTLP metric exporter for this process. Aspire injects
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>/<c>OTEL_SERVICE_NAME</c> (e.g. <c>load-0</c>), which the
    /// SDK reads itself; with no endpoint set the exporter simply has nowhere to send.
    /// </summary>
    /// <returns>The meter provider; dispose it at shutdown to flush the final export.</returns>
    public static MeterProvider BuildMeterProvider() =>
        Sdk.CreateMeterProviderBuilder()
            .AddMeter(MeterName)
            .AddView(
                ConnectDurationHistogramName,
                new ExplicitBucketHistogramConfiguration { Boundaries = [.. CalloutResponderMetrics.DurationBucketBoundariesSeconds] })
            .AddOtlpExporter()
            .Build();

    /// <summary>Records one connect attempt's client-observed latency.</summary>
    /// <param name="elapsed">Time spent in <c>ConnectAsync</c>.</param>
    /// <param name="connected">Whether the connect succeeded.</param>
    public static void RecordConnect(TimeSpan elapsed, bool connected)
    {
        ConnectDuration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("outcome", connected ? "connected" : "failed"));
        if (connected)
        {
            Interlocked.Add(ref _intervalTicks, elapsed.Ticks);
            Interlocked.Increment(ref _intervalCount);
        }
    }

    /// <summary>Returns the mean successful connect latency since the previous call as display text, and resets the window (for the console report).</summary>
    /// <returns>The mean latency in milliseconds (e.g. <c>3.42ms</c>), or <c>n/a</c> if no connect succeeded in the window.</returns>
    public static string TakeIntervalMeanConnectText()
    {
        var count = Interlocked.Exchange(ref _intervalCount, 0);
        var ticks = Interlocked.Exchange(ref _intervalTicks, 0);
        return count == 0
            ? "n/a"
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{TimeSpan.FromTicks(ticks / count).TotalMilliseconds:0.00}ms");
    }
}
