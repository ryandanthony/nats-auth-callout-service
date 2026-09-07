// -----------------------------------------------------------------------
// <copyright file="ConcurrencySettings.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// In-process parallelism and backpressure settings (doc 04 §Bootstrap <c>concurrency</c>,
/// doc 08 §In-process parallelism), driving the <c>CalloutResponder</c>'s
/// <c>Parallel.ForEachAsync</c> over <c>$SYS.REQ.USER.AUTH</c> (P07).
/// </summary>
/// <param name="MaxDegreeOfParallelism">Max auth requests processed in parallel per instance.</param>
/// <param name="PerRequestTimeout">Per-request deadline; kept below the server's callout <c>authorization.timeout</c>.</param>
/// <param name="QueueCapacity">Optional accept-queue depth before shedding, if a bounded-channel variant is used instead of the default subscription channel.</param>
public sealed record ConcurrencySettings(int MaxDegreeOfParallelism, DurationString PerRequestTimeout, int? QueueCapacity);
