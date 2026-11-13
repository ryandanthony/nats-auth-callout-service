// -----------------------------------------------------------------------
// <copyright file="HealthReportJsonWriter.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuthCallout.Host.Health;

/// <summary>
/// The health-endpoint JSON response writer (doc 08 §Health &amp; readiness: "a JSON
/// response writer exposes per-check detail for humans and dashboards... emits each check's
/// status, description, duration, and data (never secrets)"). Every value in
/// <see cref="HealthReportEntry.Data"/> is written via <see cref="object.ToString"/> rather
/// than reflected/serialized structurally, so a health check can only ever leak what it
/// deliberately stringifies into its own <c>data</c> dictionary — no check in this project
/// puts secret material there (P13's own DoD: "Health JSON never contains secret material").
/// </summary>
public static class HealthReportJsonWriter
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false };

    /// <summary>
    /// Writes <paramref name="report"/> as the health endpoint's JSON body (doc 08). Matches
    /// the shape <c>{ status, totalDurationMs, checks: [{ name, status, description,
    /// durationMs, tags, data }] }</c>.
    /// </summary>
    /// <param name="context">The HTTP context to write the response onto.</param>
    /// <param name="report">The health report to serialize.</param>
    /// <returns>A task that completes when the response body has been written.</returns>
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";

        await using var stream = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("status", report.Status.ToString());
            writer.WriteNumber("totalDurationMs", report.TotalDuration.TotalMilliseconds);
            writer.WriteStartArray("checks");
            foreach (var (name, entry) in report.Entries.OrderBy(static kvp => kvp.Key, StringComparer.Ordinal))
            {
                WriteEntry(writer, name, entry);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        stream.Position = 0;
        await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static void WriteEntry(Utf8JsonWriter writer, string name, HealthReportEntry entry)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteString("status", entry.Status.ToString());
        if (entry.Description is not null)
        {
            writer.WriteString("description", entry.Description);
        }

        writer.WriteNumber("durationMs", entry.Duration.TotalMilliseconds);

        writer.WriteStartArray("tags");
        foreach (var tag in entry.Tags)
        {
            writer.WriteStringValue(tag);
        }

        writer.WriteEndArray();

        writer.WriteStartObject("data");
        foreach (var (key, value) in entry.Data)
        {
            WriteDataValue(writer, key, value);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteDataValue(Utf8JsonWriter writer, string key, object value)
    {
        switch (value)
        {
            case string[] stringArray:
                {
                    writer.WriteStartArray(key);
                    foreach (var item in stringArray)
                    {
                        writer.WriteStringValue(item);
                    }

                    writer.WriteEndArray();
                    break;
                }

            case bool boolValue:
                {
                    writer.WriteBoolean(key, boolValue);
                    break;
                }

            case double doubleValue:
                {
                    if (double.IsFinite(doubleValue))
                    {
                        writer.WriteNumber(key, doubleValue);
                    }
                    else
                    {
                        writer.WriteString(key, doubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }

                    break;
                }

            default:
                {
                    // Every other value (numbers, timestamps, enums) is stringified: the writer
                    // is deliberately conservative here rather than reflecting arbitrary object
                    // graphs, so a health check can never accidentally leak nested secret state.
                    writer.WriteString(key, value.ToString());
                    break;
                }
        }
    }
}
