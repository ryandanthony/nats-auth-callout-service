// -----------------------------------------------------------------------
// <copyright file="ConnectBackoff.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Client.Connection;

/// <summary>
/// The unbounded exponential-backoff-with-jitter policy for transport-failure retries
/// (ADR 0021 rule 1: "retry indefinitely with exponential backoff and jitter"). A small,
/// Polly-free, <see cref="TimeProvider"/>-testable policy — deliberately not reused for the
/// auth-violation path, which is a single forced-refresh retry with no backoff (ADR 0021 rule 2).
/// </summary>
public sealed class ConnectBackoff
{
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly double _jitterRatio;
    private readonly Random _random;

    /// <summary>Initializes a new instance of the <see cref="ConnectBackoff"/> class.</summary>
    /// <param name="initialDelay">The delay after the first failed attempt. Defaults to 1 second.</param>
    /// <param name="maxDelay">The maximum delay between attempts, once reached. Defaults to 30 seconds.</param>
    /// <param name="jitterRatio">The fraction of the computed delay randomized as jitter, in [0, 1]. Defaults to 0.2 (±20%).</param>
    /// <param name="random">The random source for jitter. Defaults to a new, process-local <see cref="Random"/>.</param>
    public ConnectBackoff(TimeSpan? initialDelay = null, TimeSpan? maxDelay = null, double jitterRatio = 0.2, Random? random = null)
    {
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        _jitterRatio = jitterRatio;
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// Computes the delay before attempt number <paramref name="attempt"/> (1-based: the delay
    /// before the *first* retry, after the initial attempt already failed once).
    /// </summary>
    /// <param name="attempt">The 1-based retry attempt number (1 = first retry).</param>
    /// <returns>The delay to wait before this retry, doubling each attempt up to <see cref="_maxDelay"/>, plus jitter.</returns>
    public TimeSpan ComputeDelay(int attempt)
    {
        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "attempt must be >= 1.");
        }

        // Clamp the exponent so 2^(attempt-1) never overflows before the Min() clamp applies.
        var exponent = Math.Min(attempt - 1, 32);
        var exponential = _initialDelay * Math.Pow(2, exponent);
        var baseDelay = exponential > _maxDelay ? _maxDelay : exponential;

        if (_jitterRatio <= 0)
        {
            return baseDelay;
        }

        // Jitter in [-jitterRatio, +jitterRatio] of the base delay, never negative.
#pragma warning disable CA5394 // Jitter timing is not a security-sensitive use of randomness; a CSPRNG would be pure overhead here.
        var jitterFraction = ((_random.NextDouble() * 2) - 1) * _jitterRatio;
#pragma warning restore CA5394
        var jittered = baseDelay * (1 + jitterFraction);
        return jittered < TimeSpan.Zero ? TimeSpan.Zero : jittered;
    }
}
