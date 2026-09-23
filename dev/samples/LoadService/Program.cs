// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;
using AuthCallout.Dev.Samples.Common;
using NATS.Client.Core;

// LoadService (dev-only): a deliberately dumb load generator for the callout. Unlike
// NoteService/TaskService, it models no domain — it just hammers the auth path so the new
// callout metrics move visibly in the Aspire dashboard.
//
// Each *new* NATS connection triggers exactly one auth-callout request (the server calls out
// to $SYS.REQ.USER.AUTH on connect), so the loop below — open a connection, confirm it's up,
// close it, repeat — produces a steady stream of granted decisions. Watch these tick over live
// against the "callout" resource in the dashboard's Metrics tab:
//   - callout_requests_granted_total   (one per successful connect)
//   - callout_request_duration_seconds (receive->reply latency histogram, tagged outcome=granted)
//
// The token is fetched once (reused for every connect: re-minting per iteration would rate-limit
// Keycloak, not the callout, which is what we actually want to exercise). Tunables via env:
//   LOAD_CONCURRENCY   parallel connect loops (default 8)
//   LOAD_DELAY_MS      pause between connects within each loop (default 25ms)
//   LOAD_DURATION_SEC  total run time; 0 or unset = run until cancelled (Ctrl+C / resource stop)

const string AppSlug = "notes";

var concurrency = ReadInt("LOAD_CONCURRENCY", fallback: 8, min: 1);
var delay = TimeSpan.FromMilliseconds(ReadInt("LOAD_DELAY_MS", fallback: 25, min: 0));
var durationSeconds = ReadInt("LOAD_DURATION_SEC", fallback: 0, min: 0);

using var lifetime = new CancellationTokenSource();
if (durationSeconds > 0)
{
    lifetime.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
}

var stopSignal = lifetime;
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stopSignal.Cancel();
};

// Fetch one token + connection options through the callout (same public client + dev user as
// NoteService). This first connect is itself a callout request; the loop reuses the options.
using var startupCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var connection = await CalloutConnection.PrepareAsync(
    AppSlug, clientId: "notes-web", username: "note-user", password: "note-pass", startupCts.Token);

var totalConnects = 0L;
var totalFailures = 0L;

await Console.Out.WriteLineAsync(
    $"LoadService: driving callout connects with concurrency={concurrency}, delay={delay.TotalMilliseconds:0}ms, " +
    (durationSeconds > 0 ? $"duration={durationSeconds}s." : "until stopped."));

var workers = new Task[concurrency + 1];
for (var i = 0; i < concurrency; i++)
{
    workers[i] = DriveAsync(connection.Options, delay, lifetime.Token);
}

workers[concurrency] = ReportAsync(lifetime.Token);

await Task.WhenAll(workers);

var connects = Interlocked.Read(ref totalConnects);
await Console.Out.WriteLineAsync(
    $"LoadService: done. {connects} connects, {Interlocked.Read(ref totalFailures)} failures.");

return;

// Opens a fresh connection each iteration (= a fresh callout auth request), records the
// outcome, and pauses by the configured delay. Runs until cancelled.
async Task DriveAsync(NatsOpts opts, TimeSpan loopDelay, CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        var connected = await TryConnectOnceAsync(opts, cancellationToken);
        Interlocked.Increment(ref connected ? ref totalConnects : ref totalFailures);

        // On a failure, back off a little so a persistent error doesn't spin the CPU; otherwise
        // honor the configured inter-connect delay.
        var pause = connected ? loopDelay : TimeSpan.FromMilliseconds(250);
        await DelayAsync(pause, cancellationToken);
    }
}

// One connect attempt. ConnectAsync forces the actual connect (and thus the callout) rather
// than lazy-connecting. Returns true on success, false on any non-cancellation failure.
static async Task<bool> TryConnectOnceAsync(NatsOpts opts, CancellationToken cancellationToken)
{
    try
    {
        await using var nats = new NatsConnection(opts);
        await nats.ConnectAsync();
        return true;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        return true; // Shutting down, not a real failure.
    }
    catch (NatsException ex)
    {
        await Console.Error.WriteLineAsync($"LoadService: connect failed ({ex.GetType().Name}: {ex.Message}).");
        return false;
    }
}

// Periodic throughput line so the console shows progress even without opening the dashboard.
async Task ReportAsync(CancellationToken cancellationToken)
{
    var reportInterval = TimeSpan.FromSeconds(2);
    var last = 0L;
    while (!cancellationToken.IsCancellationRequested)
    {
        await DelayAsync(reportInterval, cancellationToken);
        var now = Interlocked.Read(ref totalConnects);
        var rate = (now - last) / reportInterval.TotalSeconds;
        last = now;
        await Console.Out.WriteLineAsync(
            $"LoadService: {now} connects so far (~{rate:0}/sec), {Interlocked.Read(ref totalFailures)} failures.");
    }
}

// A cancellation-friendly delay that swallows the cancellation exception (the caller's loop
// condition already observes the token), so callers never need their own try/catch.
static async Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
{
    if (duration <= TimeSpan.Zero)
    {
        return;
    }

    try
    {
        await Task.Delay(duration, cancellationToken);
    }
    catch (OperationCanceledException)
    {
        // Expected on shutdown; the enclosing loop's cancellation check handles the exit.
    }
}

static int ReadInt(string name, int fallback, int min)
{
    var raw = Environment.GetEnvironmentVariable(name);
    return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= min
        ? value
        : fallback;
}
