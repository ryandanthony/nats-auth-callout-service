// -----------------------------------------------------------------------
// <copyright file="CalloutResponderOptions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Nats.Callout;

/// <summary>
/// Concurrency settings for <see cref="CalloutResponder"/>, mirroring the bootstrap
/// <c>concurrency</c> block (doc 04, doc 08 §In-process parallelism). Grouped into one type
/// so <see cref="CalloutResponder"/>'s constructor stays within the repo's parameter-count
/// analyzer limit.
/// </summary>
/// <param name="MaxDegreeOfParallelism">Max auth requests processed in parallel by this instance.</param>
/// <param name="PerRequestTimeout">Per-request deadline; kept below the server's callout <c>authorization.timeout</c>.</param>
public sealed record CalloutResponderOptions(int MaxDegreeOfParallelism, TimeSpan PerRequestTimeout)
{
    /// <summary>Validates that both settings are positive, throwing <see cref="InvalidOperationException"/> otherwise.</summary>
    internal void Validate()
    {
        if (MaxDegreeOfParallelism <= 0)
        {
            throw new InvalidOperationException($"{nameof(MaxDegreeOfParallelism)} must be positive (was {MaxDegreeOfParallelism}).");
        }

        if (PerRequestTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(PerRequestTimeout)} must be positive (was {PerRequestTimeout}).");
        }
    }
}
