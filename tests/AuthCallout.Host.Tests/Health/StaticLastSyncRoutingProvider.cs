// -----------------------------------------------------------------------
// <copyright file="StaticLastSyncRoutingProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;

namespace AuthCallout.Host.Tests.Health;

/// <summary>
/// A minimal, hand-rolled <see cref="IRoutingConfigProvider"/> test double exposing a fixed
/// <see cref="LastSuccessfulSync"/>, for <see cref="ConfigSourceHealthCheck"/> tests that only
/// need the diagnostic timestamp, not a live batch stream (same pattern as the sibling
/// packets' hand-rolled Fake* seam doubles, e.g. <c>FakeActiveConfigSource</c>).
/// </summary>
internal sealed class StaticLastSyncRoutingProvider : IRoutingConfigProvider
{
    public StaticLastSyncRoutingProvider(DateTimeOffset? lastSuccessfulSync) => LastSuccessfulSync = lastSuccessfulSync;

    public DateTimeOffset? LastSuccessfulSync { get; }

    public IAsyncEnumerable<RoutingConfigBatch> WatchAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("This test double only exposes LastSuccessfulSync; it never watches.");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
