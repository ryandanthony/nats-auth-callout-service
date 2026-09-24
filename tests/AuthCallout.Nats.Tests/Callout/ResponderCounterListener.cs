// -----------------------------------------------------------------------
// <copyright file="ResponderCounterListener.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics.Metrics;
using AuthCallout.Nats.Callout;

namespace AuthCallout.Nats.Tests.Callout;

/// <summary>
/// A scoped <see cref="MeterListener"/> over <see cref="CalloutResponderMetrics"/> that tallies
/// the granted/denied/dropped counters for the duration of one test. Mirrors the pattern in
/// <c>EventEmitterTests</c>: <see cref="System.Diagnostics.Metrics"/> exposes no "current
/// value" read API (it is an export-time aggregation model), so a listener scoped to the call
/// under test is the documented way to observe a <see cref="Counter{T}"/> in-process.
/// <see cref="MeterListener.Start"/> replays <c>InstrumentPublished</c> for every currently-live
/// instrument, so this works even though the responder's meter is a process-lifetime static
/// already created by an earlier test in the run.
/// </summary>
internal sealed class ResponderCounterListener : IDisposable
{
    private readonly MeterListener _listener;
    private long _granted;
    private long _denied;
    private long _dropped;
    private readonly System.Collections.Concurrent.ConcurrentQueue<double> _durations = new();

    public ResponderCounterListener()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (string.Equals(instrument.Meter.Name, CalloutResponderMetrics.MeterName, StringComparison.Ordinal))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        _listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            if (string.Equals(instrument.Name, CalloutResponderMetrics.GrantedCounterName, StringComparison.Ordinal))
            {
                Interlocked.Add(ref _granted, measurement);
            }
            else if (string.Equals(instrument.Name, CalloutResponderMetrics.DeniedCounterName, StringComparison.Ordinal))
            {
                Interlocked.Add(ref _denied, measurement);
            }
            else if (string.Equals(instrument.Name, CalloutResponderMetrics.DroppedCounterName, StringComparison.Ordinal))
            {
                Interlocked.Add(ref _dropped, measurement);
            }
            else
            {
                // Not a counter this listener tracks; ignore. The listener is scoped to the
                // responder meter, so this branch is only reached if a new instrument is added
                // to CalloutResponderMetrics without a corresponding tally here.
            }
        });
        _listener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
        {
            if (string.Equals(instrument.Name, CalloutResponderMetrics.DurationHistogramName, StringComparison.Ordinal))
            {
                _durations.Enqueue(measurement);
            }
        });
        _listener.Start();
    }

    public long Granted => Interlocked.Read(ref _granted);

    public long Denied => Interlocked.Read(ref _denied);

    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Gets the per-request duration measurements (seconds) recorded on the histogram, in record order.</summary>
    public IReadOnlyCollection<double> Durations => _durations;

    public void Dispose() => _listener.Dispose();
}
