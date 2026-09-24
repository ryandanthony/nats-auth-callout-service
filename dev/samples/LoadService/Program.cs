// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;
using AuthCallout.Dev.Samples.LoadService;
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
// and compare against LoadService's own client-side view (each load-N resource):
//   - load_connect_duration_seconds    (full ConnectAsync as the client sees it, same buckets)
// The difference between the two is everything outside the callout's handler: TCP + NATS
// handshake, the server's $SYS.REQ.USER.AUTH round trip, and the server applying the reply.
// The console report prints the rolling mean client connect latency too.
//
// Each worker reuses one token across connects (re-minting per iteration would rate-limit
// Keycloak, not the callout, which is what we actually want to exercise) and re-mints it only
// shortly before its exp. The dev realm's accessTokenLifespan is 300s, so without the refresh
// every connect is denied as expired after ~5 minutes. Tunables via env:
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

// Disposed at exit so the final metric export is flushed.
using var meterProvider = LoadMetrics.BuildMeterProvider();

// Each worker fetches its own token, so its connections carry a distinct jti (the callout's
// identity self-lookup key, doc 07) and never contend on a single hot KV key, exactly like
// distinct real clients, which is what this load test should model. Re-minting is per worker
// near expiry, not per connect, so it stresses the callout, not Keycloak.
using var startupCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

var totalConnects = 0L;
var totalFailures = 0L;

await Console.Out.WriteLineAsync(
    $"LoadService: driving callout connects with concurrency={concurrency}, delay={delay.TotalMilliseconds:0}ms, " +
    (durationSeconds > 0 ? $"duration={durationSeconds}s." : "until stopped."));

var workers = new Task[concurrency + 1];
for (var i = 0; i < concurrency; i++)
{
    workers[i] = DriveAsync(delay, startupCts.Token, lifetime.Token);
}

workers[concurrency] = ReportAsync(lifetime.Token);

await Task.WhenAll(workers);

var connects = Interlocked.Read(ref totalConnects);
await Console.Out.WriteLineAsync(
    $"LoadService: done. {connects} connects, {Interlocked.Read(ref totalFailures)} failures.");

return;

// Fetches this worker's own token/connection options (a distinct jti so it never shares a
// self-lookup key with other workers), re-fetching shortly before the token expires, then opens
// a fresh connection each iteration (= a fresh callout auth request), records the outcome, and
// pauses by the configured delay.
async Task DriveAsync(TimeSpan loopDelay, CancellationToken startupToken, CancellationToken cancellationToken)
{
    var connection = await RefreshingCalloutConnection.CreateAsync(AppSlug, TimeProvider.System, startupToken);
    if (connection is null)
    {
        await Console.Error.WriteLineAsync("LoadService: initial worker token fetch failed; worker idle.");
        return;
    }

    while (!cancellationToken.IsCancellationRequested)
    {
        var options = await connection.GetOptionsAsync(cancellationToken);
        var connected = await TryConnectOnceAsync(options, cancellationToken);
        Interlocked.Increment(ref connected ? ref totalConnects : ref totalFailures);

        // On a failure, back off a little so a persistent error doesn't spin the CPU; otherwise
        // honor the configured inter-connect delay.
        var pause = connected ? loopDelay : TimeSpan.FromMilliseconds(250);
        await DelayAsync(pause, cancellationToken);
    }
}

// One connect attempt. ConnectAsync forces the actual connect (and thus the callout) rather
// than lazy-connecting. Returns true on success, false on any non-cancellation failure. Only
// ConnectAsync is timed (not the disposal/close), so the metric is the auth-bearing handshake.
static async Task<bool> TryConnectOnceAsync(NatsOpts opts, CancellationToken cancellationToken)
{
    var start = TimeProvider.System.GetTimestamp();
    try
    {
        await using var nats = new NatsConnection(opts);
        await nats.ConnectAsync();
        LoadMetrics.RecordConnect(TimeProvider.System.GetElapsedTime(start), connected: true);
        return true;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        return true; // Shutting down, not a real failure.
    }
    catch (NatsException ex)
    {
        LoadMetrics.RecordConnect(TimeProvider.System.GetElapsedTime(start), connected: false);
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
        var meanConnect = LoadMetrics.TakeIntervalMeanConnectText();
        await Console.Out.WriteLineAsync(
            $"LoadService: {now} connects so far (~{rate:0}/sec), mean connect {meanConnect}, {Interlocked.Read(ref totalFailures)} failures.");
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
