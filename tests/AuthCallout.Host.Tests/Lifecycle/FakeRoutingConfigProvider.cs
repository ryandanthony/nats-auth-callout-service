// -----------------------------------------------------------------------
// <copyright file="FakeRoutingConfigProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Host.Tests.Lifecycle;

/// <summary>
/// A hand-rolled <see cref="IRoutingConfigProvider"/> test double: yields batches pushed via
/// <see cref="PushAsync"/> and exposes a settable <see cref="LastSuccessfulSync"/> so
/// <see cref="AuthCallout.Host.Lifecycle.ConfigApplyLoopTests"/> can drive both the batch
/// stream and the ADR 0017 staleness clock independently, exactly like
/// <c>FakeActiveConfigSource</c> does for <see cref="IActiveConfigSource"/> in the sibling
/// P06/P09 test suites.
/// </summary>
internal sealed class FakeRoutingConfigProvider : IRoutingConfigProvider
{
    private readonly Channel<RoutingConfigBatch> _channel = Channel.CreateUnbounded<RoutingConfigBatch>();

    public DateTimeOffset? LastSuccessfulSync { get; set; }

    public async Task PushAsync(RoutingConfigBatch batch)
    {
        LastSuccessfulSync = batch.ObservedAt;
        await _channel.Writer.WriteAsync(batch).ConfigureAwait(false);
    }

    public void Complete() => _channel.Writer.TryComplete();

    public IAsyncEnumerable<RoutingConfigBatch> WatchAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
