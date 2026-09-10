// -----------------------------------------------------------------------
// <copyright file="SchemaFixture.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json.Nodes;
using Json.Schema;

namespace AuthCallout.Nats.Tests.Events;

/// <summary>
/// Loads the hand-authored JSON Schemas (copied to the test output directory alongside their
/// <c>schemas/</c> repo layout) for contract/golden tests. Same pattern as P02's
/// <c>AuthCallout.Contracts.Tests.SchemaFixture</c> (internal to each test assembly, not
/// shareable across projects without a new shared package).
/// </summary>
internal static class SchemaFixture
{
    private static readonly Dictionary<string, JsonSchema> Cache = new(StringComparer.Ordinal);

    /// <summary>Loads (and caches) the schema at a path relative to the repo's <c>schemas/</c> directory.</summary>
    /// <param name="relativePath">The schema path, e.g. <c>events/auth-outcome/v1.json</c>.</param>
    /// <returns>The parsed schema.</returns>
    public static JsonSchema Load(string relativePath)
    {
        if (Cache.TryGetValue(relativePath, out var cached))
        {
            return cached;
        }

        var fullPath = Path.Combine(AppContext.BaseDirectory, "schemas", relativePath);
        var json = File.ReadAllText(fullPath);
        var schema = JsonSchema.FromText(json);
        Cache[relativePath] = schema;
        return schema;
    }

    /// <summary>Asserts that <paramref name="instance"/> validates against the given schema.</summary>
    /// <param name="relativePath">The schema path, e.g. <c>events/auth-outcome/v1.json</c>.</param>
    /// <param name="instance">The JSON document to validate.</param>
    public static void AssertValid(string relativePath, JsonNode? instance)
    {
        var schema = Load(relativePath);
        var result = schema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(result.IsValid, DescribeFailures(relativePath, result));
    }

    private static string DescribeFailures(string relativePath, EvaluationResults result)
    {
        var details = result.Details
            .Where(static d => !d.IsValid && d.Errors is { Count: > 0 })
            .SelectMany(static d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key} = {e.Value}"));
        return $"'{relativePath}' rejected the instance:\n{string.Join('\n', details)}";
    }
}
