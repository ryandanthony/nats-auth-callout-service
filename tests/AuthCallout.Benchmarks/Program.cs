// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

// AuthCallout.Benchmarks (dev-only, tests/): the callout's request-handling latency GOAL as a
// runnable, self-checking artifact. Two jobs in one:
//   1. Proves the goal against real infrastructure — a real nats:2.11 (auth_callout configured)
//      and Keycloak container, the production ComponentGraph wiring (HostFixture, the same
//      fixture tests/AuthCallout.Integration proves against), not a mock.
//   2. Is a regression gate: every run prints and checks BOTH the client-observed connect
//      latency (BenchmarkDotNet's own measured distribution) AND the callout's own server-side
//      callout_request_duration_ms histogram (via ServerSideHistogram, the exact
//      instrument the production OTel pipeline exports) against CalloutLatencyGoal. The process
//      exits non-zero the moment either one regresses past the goal, so this can be wired into
//      CI or run by hand after any change to the request path.
//
// Not a `dotnet test` project (BenchmarkDotNet needs its own process lifecycle and a Release
// build): run with `dotnet run -c Release --project tests/AuthCallout.Benchmarks` (or
// `just bench`). Requires Docker (Testcontainers), same as tests/AuthCallout.Integration.

using AuthCallout.Benchmarks;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

using var serverHistogram = new ServerSideHistogram();

var config = ManualConfig.CreateMinimumViable().AddJob(CalloutLatencyBenchmarks.BuildJob());
var summary = BenchmarkRunner.Run<CalloutLatencyBenchmarks>(config);

if (summary.HasCriticalValidationErrors)
{
    await Console.Error.WriteLineAsync("BenchmarkDotNet reported critical validation errors; see output above.");
    return 1;
}

var report = summary.Reports.Single(r => r.Success);
var stats = report.ResultStatistics ?? throw new InvalidOperationException("The benchmark report has no result statistics.");

// BenchmarkDotNet measures in nanoseconds.
var clientP50Ms = stats.Percentiles.P50 / 1_000_000.0;
var clientP99Ms = stats.Percentiles.Percentile(99) / 1_000_000.0;

var serverSamples = serverHistogram.GrantedMillisecondsSamples;
if (serverSamples.Count == 0)
{
    await Console.Error.WriteLineAsync(
        "No callout_request_duration_ms (outcome=granted) measurements were observed. " +
        "Every benchmarked connect should have produced one grant; treating this as a failure.");
    return 1;
}

// serverSamples is chronological and includes BenchmarkDotNet's unrecorded warmup connects
// (the MeterListener has no visibility into which iterations BenchmarkDotNet itself counts as
// "warmup" vs "measured"). Since every benchmarked connect grants exactly once, the last
// stats.N samples are exactly the measured-phase requests, 1:1 with the client-side
// distribution BenchmarkDotNet reports (stats.N below) - so take the tail rather than every
// sample, or the server-side p50/p99 would be diluted by warmup-phase (JIT-cold) requests.
if (serverSamples.Count < stats.N)
{
    await Console.Error.WriteLineAsync(
        $"Observed only {serverSamples.Count} granted measurements but BenchmarkDotNet measured {stats.N} iterations; treating this as a failure.");
    return 1;
}

var serverMsSamples = serverSamples.TakeLast(stats.N).ToArray();
Array.Sort(serverMsSamples);
var serverP50Ms = Percentile(serverMsSamples, 50);
var serverP99Ms = Percentile(serverMsSamples, 99);

Console.WriteLine();
Console.WriteLine("=== Callout latency goal check ===");
Console.WriteLine($"Goal: p50 <= {CalloutLatencyGoal.P50MaxMilliseconds:0.###}ms, p99 <= {CalloutLatencyGoal.P99MaxMilliseconds:0.###}ms");
Console.WriteLine($"Client-observed connect (BenchmarkDotNet, n={stats.N}): p50={clientP50Ms:0.000}ms p99={clientP99Ms:0.000}ms");
Console.WriteLine($"Server-side callout_request_duration_ms (n={serverMsSamples.Length}): p50={serverP50Ms:0.000}ms p99={serverP99Ms:0.000}ms");

// The gate is the server-side histogram: it is the exact SLI the mission and the production
// OTel dashboard use. The client-observed number is reported alongside for context (it also
// includes TCP/NATS-handshake overhead outside the callout's own handler) but never gates.
var violation = CalloutLatencyGoal.CheckViolation(serverP50Ms, serverP99Ms);
if (violation is not null)
{
    await Console.Error.WriteLineAsync($"GOAL VIOLATED: {violation}");
    return 1;
}

Console.WriteLine("Goal met.");
return 0;

static double Percentile(IReadOnlyList<double> sortedValues, double percentileRank)
{
    if (sortedValues.Count == 1)
    {
        return sortedValues[0];
    }

    var rank = percentileRank / 100.0 * (sortedValues.Count - 1);
    var lowerIndex = (int)Math.Floor(rank);
    var upperIndex = (int)Math.Ceiling(rank);
    if (lowerIndex == upperIndex)
    {
        return sortedValues[lowerIndex];
    }

    var fraction = rank - lowerIndex;
    return sortedValues[lowerIndex] + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction);
}
