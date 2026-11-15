// -----------------------------------------------------------------------
// <copyright file="ConnectBackoffTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;

namespace AuthCallout.Client.Tests.Connection;

/// <summary>Tests for <see cref="ConnectBackoff"/>'s exponential-backoff-with-jitter math (ADR 0021 rule 1).</summary>
public sealed class ConnectBackoffTests
{
    [Fact]
    public void ComputeDelay_NoJitter_DoublesEachAttempt()
    {
        var backoff = new ConnectBackoff(initialDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(100), jitterRatio: 0);

        Assert.Equal(TimeSpan.FromSeconds(1), backoff.ComputeDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), backoff.ComputeDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(4), backoff.ComputeDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(8), backoff.ComputeDelay(4));
    }

    [Fact]
    public void ComputeDelay_NoJitter_ClampsToMaxDelay()
    {
        var backoff = new ConnectBackoff(initialDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(10), jitterRatio: 0);

        Assert.Equal(TimeSpan.FromSeconds(10), backoff.ComputeDelay(10));
        Assert.Equal(TimeSpan.FromSeconds(10), backoff.ComputeDelay(1000));
    }

    [Fact]
    public void ComputeDelay_WithJitter_StaysWithinRatioBounds()
    {
        var backoff = new ConnectBackoff(initialDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(100), jitterRatio: 0.2, random: new Random(42));

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var delay = backoff.ComputeDelay(attempt);
            var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
            Assert.InRange(delay.TotalMilliseconds, baseDelay.TotalMilliseconds * 0.8, baseDelay.TotalMilliseconds * 1.2);
        }
    }

    [Fact]
    public void ComputeDelay_AttemptLessThanOne_Throws()
    {
        var backoff = new ConnectBackoff();

        Assert.Throws<ArgumentOutOfRangeException>(() => backoff.ComputeDelay(0));
    }

    [Fact]
    public void ComputeDelay_NeverReturnsNegative()
    {
        // jitterRatio of 1 can push the jitter fraction to -1, which would be exactly zero;
        // confirm the clamp holds and nothing goes negative.
        var backoff = new ConnectBackoff(initialDelay: TimeSpan.FromMilliseconds(1), jitterRatio: 1, random: new Random(1));

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            Assert.True(backoff.ComputeDelay(attempt) >= TimeSpan.Zero);
        }
    }
}
