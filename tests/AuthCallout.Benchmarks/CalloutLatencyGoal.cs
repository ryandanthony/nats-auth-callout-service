// -----------------------------------------------------------------------
// <copyright file="CalloutLatencyGoal.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;

namespace AuthCallout.Benchmarks;

/// <summary>
/// The callout's request-handling latency goal (the mission this benchmark suite gates on):
/// p50 &lt;= 25ms, p99 &lt;= 50ms, measured server-side as <c>callout_request_duration_seconds</c>
/// (receive to reply, granted requests). Centralized here so the gate itself, the console
/// report, and any future test all read the same numbers instead of duplicating the literal.
/// </summary>
public static class CalloutLatencyGoal
{
    /// <summary>Gets the p50 ceiling in milliseconds.</summary>
    /// <value>25.</value>
    public static double P50MaxMilliseconds => 25;

    /// <summary>Gets the p99 ceiling in milliseconds.</summary>
    /// <value>50.</value>
    public static double P99MaxMilliseconds => 50;

    /// <summary>Checks a measured (p50, p99) pair against the goal.</summary>
    /// <param name="p50Milliseconds">The measured p50, in milliseconds.</param>
    /// <param name="p99Milliseconds">The measured p99, in milliseconds.</param>
    /// <returns>Null when the goal is met; otherwise a human-readable violation message.</returns>
    public static string? CheckViolation(double p50Milliseconds, double p99Milliseconds)
    {
        var violations = new List<string>();
        if (p50Milliseconds > P50MaxMilliseconds)
        {
            violations.Add(FormatLine("p50", p50Milliseconds, P50MaxMilliseconds));
        }

        if (p99Milliseconds > P99MaxMilliseconds)
        {
            violations.Add(FormatLine("p99", p99Milliseconds, P99MaxMilliseconds));
        }

        return violations.Count == 0 ? null : string.Join(" ", violations);
    }

    private static string FormatLine(string percentile, double measured, double max) =>
        string.Create(CultureInfo.InvariantCulture, $"{percentile} {measured:0.000}ms exceeds the {max:0.###}ms goal.");
}
