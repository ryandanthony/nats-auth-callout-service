// -----------------------------------------------------------------------
// <copyright file="CrdRoutingConfigProviderTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Config.K8s;
using AuthCallout.Config.K8s.Client;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Config.Tests.K8s;

/// <summary>
/// Unit tests for <see cref="CrdRoutingConfigProvider"/> against
/// <see cref="FakeCrdKubernetesClient"/> (P11 packet §Testing strategy: "unit-test the provider
/// against a faked watch-event stream"). Exercises the ADR→test map: add/modify/delete each
/// re-emit a full validated batch (ADR 0006), 410 Gone triggers a relist,
/// <see cref="IRoutingConfigProvider.LastSuccessfulSync"/> advances on a no-change relist
/// (ADR 0017), and status accepted/rejected/sibling-rejected write-back (ADR 0006, doc 09 §4).
/// </summary>
public sealed class CrdRoutingConfigProviderTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task InitialList_YieldsBatchWithListedApplications()
    {
        var client = new FakeCrdKubernetesClient();
        client.QueueList([MakeResource("notes", "notes", "NOTES")], "1");

        await using var provider = CreateProvider(client);
        using var cts = new CancellationTokenSource(TestTimeout);

        var batch = await NextBatchAsync(provider, cts.Token);

        var application = Assert.Single(batch.Routing.Applications);
        Assert.Equal("notes", application.Name);
    }

    [Fact]
    public async Task EmptyList_YieldsEmptyBatch()
    {
        var client = new FakeCrdKubernetesClient();
        client.QueueList([], "1");

        await using var provider = CreateProvider(client);
        using var cts = new CancellationTokenSource(TestTimeout);

        var batch = await NextBatchAsync(provider, cts.Token);

        Assert.Empty(batch.Routing.Applications);
    }

    [Fact]
    public async Task UnownedResource_NeverPresentedByClient_NeverAppearsInBatch()
    {
        // Label-selector ownership filtering (doc 04) happens at the ICrdKubernetesClient
        // boundary: the real CrdKubernetesClient passes the owner label selector into the K8s
        // list/watch API calls (verified live against a real cluster), so an unowned CR is never
        // returned to this provider in the first place. This test documents and locks in that
        // the provider does no filtering of its own and trusts the client completely - it must
        // never "discover" a resource the client did not hand it, even implicitly (e.g. by
        // treating a differently-owned CR sharing a name as related).
        var client = new FakeCrdKubernetesClient();
        client.QueueList([MakeResource("notes", "notes", "NOTES")], "1");

        await using var provider = CreateProvider(client);
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        var first = await NextBatchAsync(enumerator);
        Assert.Single(first.Routing.Applications);

        // Simulate the API server never delivering an unowned CR's events at all (it simply
        // never shows up on this instance's watch): no further events are emitted, and the
        // batch must stay exactly as-is.
        Assert.Null(await FirstBatchOrNullAsync(enumerator, TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task AddedEvent_ProducesNewBatchWithApplicationAdded()
    {
        var client = new FakeCrdKubernetesClient();
        client.QueueList([MakeResource("notes", "notes", "NOTES")], "1");

        await using var provider = CreateProvider(client);
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        var first = await NextBatchAsync(enumerator);
        Assert.Single(first.Routing.Applications);

        client.Emit(new CrdChangeEvent(CrdChangeKind.Added, MakeResource("tasks", "tasks", "TASKS")));

        var second = await NextBatchWithAsync(enumerator, b => b.Routing.Applications.Count == 2);
        Assert.Equal(["notes", "tasks"], second.Routing.Applications.Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ModifiedEvent_ProducesNewBatchWithUpdatedField()
    {
        var client = new FakeCrdKubernetesClient();
        client.QueueList([MakeResource("notes", "notes", "NOTES")], "1");

        await using var provider = CreateProvider(client);
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await NextBatchAsync(enumerator);

        client.Emit(new CrdChangeEvent(CrdChangeKind.Modified, MakeResource("notes", "notes", "NOTES-RENAMED")));

        var second = await NextBatchWithAsync(enumerator, b => string.Equals(b.Routing.Applications[0].TargetAccount, "NOTES-RENAMED", StringComparison.Ordinal));
        Assert.Equal("NOTES-RENAMED", second.Routing.Applications[0].TargetAccount);
    }

    [Fact]
    public async Task DeletedEvent_ProducesNewBatchWithApplicationRemoved()
    {
        var client = new FakeCrdKubernetesClient();
        client.QueueList([MakeResource("notes", "notes", "NOTES"), MakeResource("tasks", "tasks", "TASKS")], "1");

        await using var provider = CreateProvider(client);
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await NextBatchWithAsync(enumerator, b => b.Routing.Applications.Count == 2);

        client.Emit(new CrdChangeEvent(CrdChangeKind.Deleted, MakeResource("tasks", "tasks", "TASKS")));

        var second = await NextBatchWithAsync(enumerator, b => b.Routing.Applications.Count == 1);
        Assert.Equal("notes", second.Routing.Applications[0].Name);
    }

    [Fact]
    public async Task GoneException_TriggersRelist()
    {
        var client = new FakeCrdKubernetesClient();
        client.QueueList([MakeResource("notes", "notes", "NOTES")], "1");
        client.QueueList([MakeResource("notes", "notes", "NOTES"), MakeResource("tasks", "tasks", "TASKS")], "2");

        await using var provider = CreateProvider(client, minBackoff: TimeSpan.FromMilliseconds(20));
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await NextBatchWithAsync(enumerator, b => b.Routing.Applications.Count == 1);

        client.EmitGone();

        var afterRelist = await NextBatchWithAsync(enumerator, b => b.Routing.Applications.Count == 2);
        Assert.Equal(2, afterRelist.Routing.Applications.Count);
        Assert.True(client.ListCallCount >= 2);
    }

    [Fact]
    public async Task NoChangeReconcileRelist_AdvancesLastSuccessfulSync()
    {
        var client = new FakeCrdKubernetesClient();
        client.QueueList([MakeResource("notes", "notes", "NOTES")], "1");
        // Second list result is identical content, just proving a relist happened.
        client.QueueList([MakeResource("notes", "notes", "NOTES")], "2");

        await using var provider = CreateProvider(client, reconcileInterval: TimeSpan.FromMilliseconds(100));
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        var first = await NextBatchAsync(enumerator);
        var firstSync = provider.LastSuccessfulSync;
        Assert.NotNull(firstSync);

        // The reconcile interval elapses, forcing a relist even though the watch never fired
        // another event; the fake's second queued list produces an equivalent-content batch.
        var second = await NextBatchAsync(enumerator);

        Assert.True(second.ObservedAt >= first.ObservedAt);
        Assert.True(provider.LastSuccessfulSync >= firstSync);
        Assert.Equal(first.Routing, second.Routing);
    }

    [Fact]
    public async Task ValidBatch_WritesAcceptedStatusForEveryResource()
    {
        var client = new FakeCrdKubernetesClient();
        client.QueueList([MakeResource("notes", "notes", "NOTES")], "1");

        await using var provider = CreateProvider(client);
        using var cts = new CancellationTokenSource(TestTimeout);
        await NextBatchAsync(provider, cts.Token);

        await WaitForStatusPatchesAsync(client, expectedCount: 1);

        var patch = Assert.Single(client.StatusPatches);
        Assert.True(patch.Accepted);
        Assert.Empty(patch.Reason);
    }

    [Fact]
    public async Task InvalidBatch_WritesRejectedStatus_WithOwnAndSiblingReasons()
    {
        var client = new FakeCrdKubernetesClient();
        client.QueueList(
            [MakeResource("notes", "notes", "NOTES"), MakeMalformedResource("broken", "broken")],
            "1");

        await using var provider = CreateProvider(client);
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        // No batch is ever emitted (ADR 0006); calling MoveNextAsync starts the iterator (and
        // therefore the watch supervisor) without assuming it ever completes with a batch, then
        // we drive status-write assertions off the fake client's recorded patches instead.
        Assert.Null(await FirstBatchOrNullAsync(enumerator, TimeSpan.FromMilliseconds(200)));

        await WaitForStatusPatchesAsync(client, expectedCount: 2);

        var brokenPatch = client.StatusPatches.Single(p => string.Equals(p.Resource.Name, "broken", StringComparison.Ordinal));
        Assert.False(brokenPatch.Accepted);
        Assert.Contains("targetAccount", brokenPatch.Reason, StringComparison.Ordinal);

        var notesPatch = client.StatusPatches.Single(p => string.Equals(p.Resource.Name, "notes", StringComparison.Ordinal));
        Assert.False(notesPatch.Accepted);
        Assert.Contains("sibling", notesPatch.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WatchAsync_CalledTwice_Throws()
    {
        var client = new FakeCrdKubernetesClient();
        client.QueueList([], "1");
        await using var provider = CreateProvider(client);
        using var cts = new CancellationTokenSource();

        _ = provider.WatchAsync(cts.Token);

        Assert.Throws<InvalidOperationException>(() => provider.WatchAsync(cts.Token));
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var client = new FakeCrdKubernetesClient();
        client.QueueList([], "1");
        var provider = CreateProvider(client);
        try
        {
            using var cts = new CancellationTokenSource(TestTimeout);
            await NextBatchAsync(provider, cts.Token);

            await provider.DisposeAsync();

            var exception = await Record.ExceptionAsync(() => provider.DisposeAsync().AsTask());
            Assert.Null(exception);
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    private static CrdRoutingConfigProvider CreateProvider(
        FakeCrdKubernetesClient client, TimeSpan? minBackoff = null, TimeSpan? reconcileInterval = null) =>
        new(
            client,
            disposeClient: false,
            minBackoff: minBackoff ?? TimeSpan.FromMilliseconds(20),
            maxBackoff: TimeSpan.FromMilliseconds(200),
            reconcileInterval: reconcileInterval ?? TimeSpan.FromMinutes(10));

    private static CustomResourceRecord MakeResource(string ns, string name, string targetAccount)
    {
        var specJson = $$"""
            {
              "targetAccount": "{{targetAccount}}",
              "issuer": {
                "authority": "https://id.example.com/realms/{{name}}",
                "jwksCache": "5m",
                "validateAudience": false
              },
              "profiles": []
            }
            """;
        using var doc = JsonDocument.Parse(specJson);
        return new CustomResourceRecord(name, ns, Generation: 1, doc.RootElement.Clone());
    }

    private static CustomResourceRecord MakeMalformedResource(string ns, string name)
    {
        using var doc = JsonDocument.Parse("""{ "profiles": [] }""");
        return new CustomResourceRecord(name, ns, Generation: 1, doc.RootElement.Clone());
    }

    private static async Task<RoutingConfigBatch> NextBatchAsync(CrdRoutingConfigProvider provider, CancellationToken token)
    {
        var enumerator = provider.WatchAsync(token).GetAsyncEnumerator(token);
        return await NextBatchAsync(enumerator);
    }

    private static async Task<RoutingConfigBatch> NextBatchAsync(IAsyncEnumerator<RoutingConfigBatch> enumerator)
    {
        if (!await enumerator.MoveNextAsync())
        {
            throw new InvalidOperationException("Expected another batch but the sequence ended.");
        }

        return enumerator.Current;
    }

    private static async Task<RoutingConfigBatch> NextBatchWithAsync(
        IAsyncEnumerator<RoutingConfigBatch> enumerator, Func<RoutingConfigBatch, bool> predicate)
    {
        while (true)
        {
            var batch = await NextBatchAsync(enumerator);
            if (predicate(batch))
            {
                return batch;
            }
        }
    }

    private static async Task<RoutingConfigBatch?> FirstBatchOrNullAsync(IAsyncEnumerator<RoutingConfigBatch> enumerator, TimeSpan within)
    {
        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        var winner = await Task.WhenAny(moveNextTask, Task.Delay(within, CancellationToken.None));
        if (winner != moveNextTask)
        {
            return null;
        }

        return await moveNextTask ? enumerator.Current : null;
    }

    private static async Task WaitForStatusPatchesAsync(FakeCrdKubernetesClient client, int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow + TestTimeout;
        while (client.StatusPatches.Count < expectedCount && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(expectedCount, client.StatusPatches.Count);
    }
}
