// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;
using AuthCallout.Dev.Samples.Common;
using NATS.Client.Core;

// LoadService (dev-only): a deliberately dumb load generator for the callout. Unlike
// NoteService/TaskService, it models no domain — it just hammers the auth path so the new
// callout metrics move visibly in the Aspire dashboard.
//
// Each *new* NATS connection triggers exactly one auth-callout request (the server calls out
// to $SYS.REQ.USER.AUTH on connect), so the loop below — open a connection, confirm it's up,
// close it, repeat — produces a steady stream of granted decisions. Watch these tick over live:
//   - callout_requests_granted_total   (one per successful connect)
//   - callout_request_duration_seconds (receive->reply latency histogram, tagged outcome=granted)
// against the "callout" resource in the dashboard's Metrics tab.
//
// The token is fetched once (reused for every connect: re-minting per iteration would rate-limit
// Keycloak, not the callout, which is what we actually want to exercise). Tunables via env:
//   LOAD_CONCURRENCY   parallel connect loops (default 8)
//   LOAD_DELAY_MS      pause between connects within each loop (default 25ms)
//   LOAD_DURATION_SEC  total run time; 0 or unset = run until cancelled (Ctrl+C / resource stop)

const string AppSlug = "notes";

var concurrency = ReadInt("LOAD_CONCURRENCY", 8, min: 1);
var delay = TimeSpan.FromMilliseconds(ReadInt("LOAD_DELAY_MS", 25, min: 0));
var durationSeconds = ReadInt("LOAD_DURATION_SEC", 0, min: 0);

using var lifetime = new CancellationTokenSource();
if (durationSeconds > 0)
{
    lifetime.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
}

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    lifetime.Cancel();
};

// Fetch one token + connection options through the callout (same public client + dev user as
// NoteService). This first connect is itself a callout request; the loop reuses the options.
using var startupCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var connection = await CalloutConnection.PrepareAsync(
    AppSlug, clientId: "notes-web", username: "note-user", password: "note-pass", startupCts.Token);

Console.WriteLine(
    $"LoadService: driving callout connects with concurrency={concurrency}, delay={delay.TotalMilliseconds:0}ms, " +
    (durationSeconds > 0 ? $"duration={durationSeconds}s." : "until stopped."));

long totalConnects = 0;
long totalFailures = 0;
var overall = Stopwatch.StartNew();

var workers = Enumerable.Range(0, concurrency)
    .Select(_ => Task.Run(() => DriveAsync(connection.Options, delay, lifetime.Token, () =>
    {
        Interlocked.Increment(ref totalConnects);
    }, () =>
    {
        Interlocked.Increment(ref totalFailures);
    })))
    .ToArray();

// Periodic progress line so the console shows throughput even without opening the dashboard.
var reporter = Task.Run(async () =>
{
    var last = 0L;
    while (!lifetime.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }

        var now = Interlocked.Read(ref totalConnects);
        var rate = (now - last) / 2.0;
        last = now;
        Console.WriteLine($"LoadService: {now} connects so far (~{rate:0} connects/sec), {Interlocked.Read(ref totalFailures)} failures.");
    }
});

await Task.WhenAll(workers);
await reporter;
overall.Stop();

var connects = Interlocked.Read(ref totalConnects);
Console.WriteLine(
    $"LoadService: done. {connects} connects in {overall.Elapsed.TotalSeconds:0.0}s " +
    $"(~{connects / Math.Max(overall.Elapsed.TotalSeconds, 0.001):0} connects/sec), {Interlocked.Read(ref totalFailures)} failures.");

return;

static async Task DriveAsync(NatsOpts opts, TimeSpan delay, CancellationToken cancellationToken, Action onConnect, Action onFailure)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            // A fresh connection each iteration = a fresh callout auth request. ConnectAsync
            // forces the actual connect (and thus the callout) rather than lazy-connecting.
            await using var nats = new NatsConnection(opts);
            await nats.ConnectAsync();
            onConnect();
            await nats.DisposeAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            onFailure();
            Console.Error.WriteLine($"LoadService: connect failed ({ex.GetType().Name}: {ex.Message}).");
            // Back off briefly so a persistent failure doesn't spin the CPU.
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

static int ReadInt(string name, int fallback, int min)
{
    var raw = Environment.GetEnvironmentVariable(name);
    return int.TryParse(raw, out var value) && value >= min ? value : fallback;
}
