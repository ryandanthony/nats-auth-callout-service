// -----------------------------------------------------------------------
// <copyright file="FileRoutingConfigProviderTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Config.Files;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Config.Tests.Files;

/// <summary>
/// Integration-style tests for <see cref="FileRoutingConfigProvider"/> against real temporary
/// directories (P10 packet DoD: "real temp directories, no mocking of the FS"). Exercises the
/// ADR→test map: add/remove-file batch changes, debounce coalescing, one-bad-file-rejects-batch,
/// and <see cref="IRoutingConfigProvider.LastSuccessfulSync"/> advancing on no-change scans
/// (ADR 0017).
/// </summary>
public sealed class FileRoutingConfigProviderTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly string _directory;

    public FileRoutingConfigProviderTests()
    {
        _directory = Directory.CreateTempSubdirectory("authcallout-files-provider-tests-").FullName;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a lingering temp directory does not fail the test run.
        }
    }

    [Fact]
    public async Task EmptyDirectory_YieldsEmptyBatch()
    {
        await using var provider = CreateProvider();
        using var cts = new CancellationTokenSource(TestTimeout);

        var batch = await FirstBatchAsync(provider, cts.Token);

        Assert.Empty(batch.Routing.Applications);
    }

    [Fact]
    public async Task SingleValidApplicationFile_YieldsBatchWithThatApplication()
    {
        WriteApplicationFile("notes.json", "notes", "NOTES");

        await using var provider = CreateProvider();
        using var cts = new CancellationTokenSource(TestTimeout);

        var batch = await FirstBatchAsync(provider, cts.Token);

        var application = Assert.Single(batch.Routing.Applications);
        Assert.Equal("notes", application.Name);
    }

    [Fact]
    public async Task AddingFile_ProducesNewBatchWithApplicationAdded()
    {
        WriteApplicationFile("notes.json", "notes", "NOTES");

        await using var provider = CreateProvider();
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        var first = await NextBatchAsync(enumerator);
        Assert.Single(first.Routing.Applications);

        WriteApplicationFile("tasks.json", "tasks", "TASKS");

        var second = await NextBatchWithAsync(enumerator, batch => batch.Routing.Applications.Count == 2);
        Assert.Equal(["notes", "tasks"], second.Routing.Applications.Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task RemovingFile_ProducesNewBatchWithApplicationRemoved()
    {
        WriteApplicationFile("notes.json", "notes", "NOTES");
        var tasksPath = WriteApplicationFile("tasks.json", "tasks", "TASKS");

        await using var provider = CreateProvider();
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        var first = await NextBatchWithAsync(enumerator, batch => batch.Routing.Applications.Count == 2);
        Assert.Equal(2, first.Routing.Applications.Count);

        File.Delete(tasksPath);

        var second = await NextBatchWithAsync(enumerator, batch => batch.Routing.Applications.Count == 1);
        var remaining = Assert.Single(second.Routing.Applications);
        Assert.Equal("notes", remaining.Name);
    }

    [Fact]
    public async Task OneInvalidFile_RejectsWholeBatch_ErrorNamesTheFile()
    {
        WriteApplicationFile("notes.json", "notes", "NOTES");
        await File.WriteAllTextAsync(Path.Combine(_directory, "broken.json"), "{ this is not valid json", CancellationToken.None);

        await using var provider = CreateProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        // No batch should ever be emitted: the whole aggregate is rejected (ADR 0006), and the
        // only file present besides the broken one is otherwise-valid, so success would be a bug.
        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        var completed = await Task.WhenAny(moveNextTask, Task.Delay(TimeSpan.FromMilliseconds(400), CancellationToken.None));

        Assert.NotEqual(moveNextTask, completed);
        Assert.NotNull(provider.LastError);
        Assert.Contains("broken.json", provider.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RapidWrites_DebounceToOneBatch()
    {
        // Debounce window (500ms) is set well above the inter-write gap so the writes coalesce
        // deterministically: the watcher resets its timer on each event, so as long as no gap
        // between writes exceeds the window they collapse to one batch. A generous window keeps
        // this robust to CI scheduling jitter stalling a Task.Delay past a tighter window (which
        // would fire an early batch and split the writes).
        await using var provider = CreateProvider(debounceWindow: TimeSpan.FromMilliseconds(500), reconcileInterval: TimeSpan.FromMinutes(10));
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        // Drain the initial (empty-directory) batch first.
        await NextBatchAsync(enumerator);

        var path = WriteApplicationFile("notes.json", "notes", "NOTES");
        for (var i = 0; i < 5; i++)
        {
            File.SetLastWriteTimeUtc(path, DateTimeOffset.UtcNow.UtcDateTime);
            await Task.Delay(20, cts.Token);
        }

        // Idle timeout (1.5s) must exceed the debounce window so the collector waits for the
        // single debounced batch to arrive (rather than declaring idle before it fires), while
        // still catching a hypothetical second batch within the 4s collect window.
        var batches = await CollectBatchesWithinAsync(enumerator, TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(1500));

        // The reconcile timer is set far in the future, so every batch observed here came from
        // the debounced watcher path; five rapid writes should coalesce into exactly one.
        Assert.Single(batches);
        Assert.Single(batches[0].Routing.Applications);
    }

    [Fact]
    public async Task LastSuccessfulSync_AdvancesOnNoChangeReconcileScan()
    {
        WriteApplicationFile("notes.json", "notes", "NOTES");

        await using var provider = CreateProvider(reconcileInterval: TimeSpan.FromMilliseconds(150));
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        var first = await NextBatchAsync(enumerator);
        var firstSync = provider.LastSuccessfulSync;
        Assert.NotNull(firstSync);
        Assert.Equal(first.ObservedAt, firstSync);

        // No file-system change follows; the next batch must come from the periodic
        // reconciliation relist, and LastSuccessfulSync must advance anyway (ADR 0017).
        var second = await NextBatchAsync(enumerator);

        Assert.True(second.ObservedAt > first.ObservedAt);
        Assert.Equal(second.ObservedAt, provider.LastSuccessfulSync);
        Assert.Equal(first.Routing, second.Routing);
    }

    [Fact]
    public void WatchAsync_CalledTwice_Throws()
    {
        var provider = CreateProvider();
        using var cts = new CancellationTokenSource();

        _ = provider.WatchAsync(cts.Token);

        Assert.Throws<InvalidOperationException>(() => provider.WatchAsync(cts.Token));
    }

    [Fact]
    public async Task DisposeAsync_StopsWatchLoopGracefully()
    {
        var provider = CreateProvider();
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await NextBatchAsync(enumerator);

        await provider.DisposeAsync();

        // Second dispose must be a harmless no-op; disposing twice completing without throwing
        // is the behavior under test.
        var exception = await Record.ExceptionAsync(() => provider.DisposeAsync().AsTask());
        Assert.Null(exception);
    }

    private FileRoutingConfigProvider CreateProvider(TimeSpan? debounceWindow = null, TimeSpan? reconcileInterval = null) =>
        new(
            new FilesSourceSettings(_directory),
            debounceWindow: debounceWindow ?? TimeSpan.FromMilliseconds(50),
            reconcileInterval: reconcileInterval ?? TimeSpan.FromMinutes(10));

    private string WriteApplicationFile(string fileName, string appName, string targetAccount)
    {
        var path = Path.Combine(_directory, fileName);
        var content = $$"""
            {
              "schemaVersion": 1,
              "name": "{{appName}}",
              "targetAccount": "{{targetAccount}}",
              "issuer": {
                "authority": "https://id.example.com/realms/{{appName}}",
                "jwksCache": "5m",
                "validateAudience": false
              },
              "profiles": []
            }
            """;
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task<RoutingConfigBatch> FirstBatchAsync(FileRoutingConfigProvider provider, CancellationToken token)
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

    /// <summary>
    /// Collects every batch that arrives within <paramref name="collectWindow"/>, stopping
    /// early if no further batch arrives within <paramref name="idleTimeout"/>.
    /// </summary>
    private static async Task<List<RoutingConfigBatch>> CollectBatchesWithinAsync(
        IAsyncEnumerator<RoutingConfigBatch> enumerator, TimeSpan collectWindow, TimeSpan idleTimeout)
    {
        var batches = new List<RoutingConfigBatch>();
        var collectDeadline = DateTimeOffset.UtcNow + collectWindow;
        var idle = false;

        while (!idle && DateTimeOffset.UtcNow < collectDeadline)
        {
            var moveNextTask = enumerator.MoveNextAsync().AsTask();
            var winner = await Task.WhenAny(moveNextTask, Task.Delay(idleTimeout, CancellationToken.None));
            if (winner == moveNextTask && await moveNextTask)
            {
                batches.Add(enumerator.Current);
            }
            else
            {
                idle = true;
            }
        }

        return batches;
    }
}
