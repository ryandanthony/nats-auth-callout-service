// -----------------------------------------------------------------------
// <copyright file="Recorder.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace AuthCallout.Integration.Scenarios;

/// <summary>
/// The static recorder <see cref="PingHandlers"/> writes to and every scenario reads from
/// (process-wide, since Wolverine handler static classes have no per-host instance scope to
/// hang a recorder off); concurrency-safe collections, filtered by each test's own
/// GUID-distinguished payload rather than ever cleared, so concurrent test classes never
/// interfere with each other's assertions.
/// </summary>
public static class Recorder
{
    /// <summary>Gets every <see cref="Ping"/> this process's handler has handled, as (payload, verified subject) pairs.</summary>
    /// <value>The handled pings.</value>
    public static ConcurrentBag<(string payload, string subject)> PingHandled { get; } = new();

    /// <summary>Gets every <see cref="AnonymousPing"/> payload this process's handler has handled.</summary>
    /// <value>The handled anonymous-ping payloads.</value>
    public static ConcurrentBag<string> AnonymousPingHandled { get; } = new();
}
