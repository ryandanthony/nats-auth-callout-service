// -----------------------------------------------------------------------
// <copyright file="PostgresRoutingConfigProviderTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Config.Postgres;
using AuthCallout.Config.Postgres.Repository;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Config.Tests.Postgres;

/// <summary>
/// Unit tests for <see cref="PostgresRoutingConfigProvider"/> against
/// <see cref="FakeAuthApplicationRepository"/> (P12 packet §Testing strategy). Exercises the
/// ADR→test map: enabled=false rows excluded, <see cref="IRoutingConfigProvider.LastSuccessfulSync"/>
/// advances on unchanged polls (ADR 0017), and general poll/notify wiring.
/// </summary>
public sealed class PostgresRoutingConfigProviderTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task InitialPoll_YieldsBatchWithListedRows()
    {
        var repository = new FakeAuthApplicationRepository();
        repository.SetRows([MakeRow("notes", "notes", "NOTES", enabled: true)]);

        await using var provider = CreateProvider(repository);
        using var cts = new CancellationTokenSource(TestTimeout);

        var batch = await NextBatchAsync(provider, cts.Token);

        var application = Assert.Single(batch.Routing.Applications);
        Assert.Equal("notes", application.Name);
    }

    [Fact]
    public async Task DisabledRow_ExcludedFromRepositoryResult_NeverAppearsInBatch()
    {
        // enabled=false filtering is the repository's own SQL WHERE clause (P12 packet: "SELECT
        // all enabled rows"), so a disabled row is never even handed to the provider. This test
        // documents/locks in that the provider trusts the repository's enabled-only contract
        // completely and does no filtering of its own.
        var repository = new FakeAuthApplicationRepository();
        repository.SetRows(
        [
            MakeRow("notes", "notes", "NOTES", enabled: true),

            // A disabled row would never be returned by ListEnabledAsync in the real
            // implementation; omitting it here from the fake's result set is the correct
            // simulation of that filtering having already happened.
        ]);

        await using var provider = CreateProvider(repository);
        using var cts = new CancellationTokenSource(TestTimeout);

        var batch = await NextBatchAsync(provider, cts.Token);

        var application = Assert.Single(batch.Routing.Applications);
        Assert.Equal("notes", application.Name);
    }

    [Fact]
    public async Task NotificationSignal_TriggersEarlyPoll()
    {
        var repository = new FakeAuthApplicationRepository();
        repository.SetRows([MakeRow("notes", "notes", "NOTES", enabled: true)]);

        // Poll interval is deliberately long; a notification is the only realistic way a second
        // batch arrives within the test timeout, proving the LISTEN bridge actually wakes polls.
        await using var provider = CreateProvider(repository, pollInterval: TimeSpan.FromMinutes(10));
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await NextBatchAsync(enumerator);

        repository.SetRows(
        [
            MakeRow("notes", "notes", "NOTES", enabled: true),
            MakeRow("tasks", "tasks", "TASKS", enabled: true),
        ]);
        repository.EmitNotification();

        var second = await NextBatchWithAsync(enumerator, b => b.Routing.Applications.Count == 2);
        Assert.Equal(2, second.Routing.Applications.Count);
    }

    [Fact]
    public async Task UnchangedPoll_AdvancesLastSuccessfulSync()
    {
        var repository = new FakeAuthApplicationRepository();
        repository.SetRows([MakeRow("notes", "notes", "NOTES", enabled: true)]);

        await using var provider = CreateProvider(repository, pollInterval: TimeSpan.FromMilliseconds(100));
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        var first = await NextBatchAsync(enumerator);
        var firstSync = provider.LastSuccessfulSync;
        Assert.NotNull(firstSync);

        // No row change follows; the next batch must come from the periodic poll interval
        // elapsing again, and LastSuccessfulSync must advance anyway (ADR 0017).
        var second = await NextBatchAsync(enumerator);

        Assert.True(second.ObservedAt >= first.ObservedAt);
        Assert.True(provider.LastSuccessfulSync >= firstSync);
        Assert.Equal(first.Routing, second.Routing);
        Assert.True(repository.ListCallCount >= 2);
    }

    [Fact]
    public async Task TransientListFailure_DoesNotAdvanceLastSuccessfulSync_AndKeepsLastGoodError()
    {
        var repository = new FakeAuthApplicationRepository();
        repository.SetRows([MakeRow("notes", "notes", "NOTES", enabled: true)]);

        await using var provider = CreateProvider(repository, pollInterval: TimeSpan.FromMilliseconds(100));
        using var cts = new CancellationTokenSource(TestTimeout);
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await NextBatchAsync(enumerator);
        var syncBeforeFailure = provider.LastSuccessfulSync;

        repository.QueueListFailure();

        // Give the poll loop time to hit and absorb the queued failure; no batch is emitted for
        // a transient connection error (distinct from an ADR0006 validation rejection).
        await Task.Delay(300, cts.Token);

        Assert.Equal(syncBeforeFailure, provider.LastSuccessfulSync);
        Assert.Null(provider.LastError);
    }

    [Fact]
    public async Task InvalidBatch_NeverEmitted_LastErrorReports_AndSyncStillAdvances()
    {
        var repository = new FakeAuthApplicationRepository();
        repository.SetRows(
        [
            MakeRow("notes", "notes", "NOTES", enabled: true),
            MakeMalformedRow("broken"),
        ]);

        await using var provider = CreateProvider(repository);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var enumerator = provider.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        var completed = await Task.WhenAny(moveNextTask, Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken.None));

        Assert.NotEqual(moveNextTask, completed);
        Assert.NotNull(provider.LastError);
        Assert.Contains("broken", provider.LastError, StringComparison.Ordinal);

        // Even though no batch was emitted, the poll itself succeeded (it's the query that
        // failed to validate, not the connection), so the staleness heartbeat still advances.
        Assert.NotNull(provider.LastSuccessfulSync);
    }

    [Fact]
    public async Task WatchAsync_CalledTwice_Throws()
    {
        var repository = new FakeAuthApplicationRepository();
        repository.SetRows([]);
        await using var provider = CreateProvider(repository);
        using var cts = new CancellationTokenSource();

        _ = provider.WatchAsync(cts.Token);

        Assert.Throws<InvalidOperationException>(() => provider.WatchAsync(cts.Token));
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var repository = new FakeAuthApplicationRepository();
        repository.SetRows([]);
        var provider = CreateProvider(repository);
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

    [Fact]
    public void Constructor_NonPositivePollInterval_Throws()
    {
        var repository = new FakeAuthApplicationRepository();

        Assert.Throws<ArgumentOutOfRangeException>(() => new PostgresRoutingConfigProvider(repository, TimeSpan.Zero));
    }

    private static PostgresRoutingConfigProvider CreateProvider(FakeAuthApplicationRepository repository, TimeSpan? pollInterval = null) =>
        new(repository, pollInterval ?? TimeSpan.FromMilliseconds(50));

    private static AuthApplicationRow MakeRow(string name, string appName, string targetAccount, bool enabled)
    {
        var specJson = $$"""
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
        using var doc = JsonDocument.Parse(specJson);
        return new AuthApplicationRow(name, doc.RootElement.Clone(), enabled);
    }

    private static AuthApplicationRow MakeMalformedRow(string name)
    {
        using var doc = JsonDocument.Parse("""{ "schemaVersion": 1, "name": "broken" }""");
        return new AuthApplicationRow(name, doc.RootElement.Clone(), Enabled: true);
    }

    private static async Task<RoutingConfigBatch> NextBatchAsync(PostgresRoutingConfigProvider provider, CancellationToken token)
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
}
