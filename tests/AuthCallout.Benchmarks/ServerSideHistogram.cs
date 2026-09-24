// -----------------------------------------------------------------------
// <copyright file="ServerSideHistogram.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics.Metrics;
using AuthCallout.Nats.Callout;

namespace AuthCallout.Benchmarks;

/// <summary>
/// Subscribes directly to the callout's own <see cref="CalloutResponderMetrics.RequestDuration"/>
/// instrument via <see cref="MeterListener"/> and records every raw measurement in-process, so
/// this suite reports the exact same server-side receive-to-reply latency
/// (<c>callout_request_duration_seconds</c>) the production OTel pipeline would export — not a
/// client-side approximation. <see cref="HostFixture"/> runs the real production
/// <c>CalloutResponder</c> in the same process as this benchmark, so the meter is directly
/// observable without an OTLP hop.
/// </summary>
public sealed class ServerSideHistogram : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<double> _grantedSecondsSamples = [];
    private readonly Lock _sync = new();

    /// <summary>Initializes a new instance of the <see cref="ServerSideHistogram"/> class and starts listening.</summary>
    public ServerSideHistogram()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name.Equals(CalloutResponderMetrics.MeterName, StringComparison.Ordinal)
                && instrument.Name.Equals(CalloutResponderMetrics.DurationHistogramName, StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<double>(OnMeasurement);
        _listener.Start();
    }

    /// <summary>Gets the granted-outcome samples recorded since construction, in seconds (callout's own unit).</summary>
    /// <value>A snapshot of every granted <c>callout_request_duration_seconds</c> measurement observed so far.</value>
    public IReadOnlyList<double> GrantedSecondsSamples
    {
        get
        {
            lock (_sync)
            {
                return [.. _grantedSecondsSamples];
            }
        }
    }

    /// <summary>Discards every sample recorded so far (call between a warmup phase and the measured phase).</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _grantedSecondsSamples.Clear();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _listener.Dispose();

#pragma warning disable S4017 // The nested generic (ReadOnlySpan<KeyValuePair<string, object?>>) is MeterListener's own fixed callback signature (SetMeasurementEventCallback<double>); a wrapper type here would just re-wrap the same shape for no benefit.
    private void OnMeasurement(Instrument instrument, double measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
#pragma warning restore S4017
    {
        foreach (var tag in tags)
        {
            if (tag is { Key: "outcome", Value: "granted" })
            {
                lock (_sync)
                {
                    _grantedSecondsSamples.Add(measurement);
                }

                return;
            }
        }
    }
}
