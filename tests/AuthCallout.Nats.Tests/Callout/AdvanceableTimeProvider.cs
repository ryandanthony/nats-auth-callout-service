// -----------------------------------------------------------------------
// <copyright file="AdvanceableTimeProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Nats.Tests.Callout;

/// <summary>
/// A controllable monotonic <see cref="TimeProvider"/> for timing tests: exposes a
/// <see cref="Advance"/> method that moves the high-frequency timestamp forward by a known
/// amount, so a test can make the responder's recorded request-duration latency deterministic.
/// Overrides only the timestamp surface (<see cref="GetTimestamp"/> and
/// <see cref="TimestampFrequency"/>) that <see cref="TimeProvider.GetElapsedTime(long)"/> reads;
/// wall-clock members keep the system defaults, which the responder never uses.
/// </summary>
internal sealed class AdvanceableTimeProvider : TimeProvider
{
    // 1 tick == 1 nanosecond, so the frequency is 1e9 ticks/second — an easy-to-reason-about
    // resolution well above the millisecond amounts these tests advance by.
    private const long Frequency = 1_000_000_000;

    private long _timestamp;

    public override long TimestampFrequency => Frequency;

    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

    /// <summary>Advances the monotonic timestamp by <paramref name="delta"/>.</summary>
    public void Advance(TimeSpan delta) =>
        Interlocked.Add(ref _timestamp, (long)(delta.TotalSeconds * Frequency));
}
