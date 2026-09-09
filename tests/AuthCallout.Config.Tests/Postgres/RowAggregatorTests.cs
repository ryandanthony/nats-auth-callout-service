// -----------------------------------------------------------------------
// <copyright file="RowAggregatorTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Config.Postgres.Aggregation;
using AuthCallout.Config.Postgres.Repository;
using AuthCallout.Config.Tests.CrossSource;

namespace AuthCallout.Config.Tests.Postgres;

/// <summary>
/// Unit tests for <see cref="RowAggregator"/> (P12 packet). Pure, repository-free: exercises the
/// ADR→test map's "row spec → identical canonical model as equivalent file" (shared golden
/// contract test, ADR 0007), "bad row rejects batch, names it" (ADR 0006), and basic
/// aggregation semantics.
/// </summary>
public sealed class RowAggregatorTests
{
    /// <summary>The doc09 <c>notes</c> example, as the full file-schema-v1 JSON document (the exact JSONB <c>spec</c> shape, P12's design doc).</summary>
    private const string NotesSpecJson = """
        {
          "schemaVersion": 1,
          "name": "notes",
          "targetAccount": "NOTES",
          "issuer": {
            "authority": "https://id.ants.zone/realms/main",
            "jwksCache": "5m",
            "validateAudience": true,
            "audience": ["nats"],
            "clockSkew": "60s",
            "allowedAlgorithms": ["RS256", "ES256"]
          },
          "match": { "azp": ["notes-web"] },
          "identityPropagation": { "enabled": true },
          "anonymousProfile": { "subscribe": ["notes.public.>"] },
          "events": {
            "success": "notes.events.auth.success",
            "failure": "notes.events.auth.failure"
          },
          "profiles": [
            {
              "name": "editor",
              "match": { "realm_access.roles": ["notes-editor"] },
              "grant": {
                "publish": ["notes.{tenant}.>"],
                "subscribe": ["notes.{tenant}.>"]
              },
              "nameClaim": "sub",
              "autoAddSessionAccess": false,
              "variables": [
                { "name": "tenant", "claim": "tenant_id", "required": true, "transforms": ["lowercase", "sanitize"] }
              ],
              "publishTemplates": ["notes.{tenant}.outbox.>"],
              "subscribeTemplates": ["notes.{tenant}.inbox.>"]
            }
          ]
        }
        """;

    [Fact]
    public void EmptyRowSet_AggregatesToEmptyValidBatch()
    {
        var decision = RowAggregator.Aggregate([]);

        Assert.True(decision.IsSuccess);
        Assert.Empty(decision.Routing!.Applications);
    }

    [Fact]
    public void Doc09RowExample_MapsToExpectedCanonicalModel()
    {
        var row = MakeRow("notes", NotesSpecJson);

        var decision = RowAggregator.Aggregate([row]);

        Assert.True(decision.IsSuccess, decision.Error);
        var application = Assert.Single(decision.Routing!.Applications);
        Assert.Equal(CanonicalExamples.NotesApplication, application);
    }

    [Fact]
    public void MultipleValidRows_AllAppearInAggregate()
    {
        var rows = new[]
        {
            MakeRow("notes", NotesSpecJson),
            MakeRow("tasks", MakeMinimalSpecJson("tasks", "TASKS")),
        };

        var decision = RowAggregator.Aggregate(rows);

        Assert.True(decision.IsSuccess, decision.Error);
        Assert.Equal(2, decision.Routing!.Applications.Count);
        Assert.Equal(["notes", "tasks"], decision.Routing.Applications.Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void OneMalformedRow_RejectsWholeBatch_ErrorNamesTheRow()
    {
        var rows = new[]
        {
            MakeRow("notes", NotesSpecJson),
            MakeRow("broken", """{ "schemaVersion": 1, "name": "broken" }"""),
        };

        var decision = RowAggregator.Aggregate(rows);

        Assert.False(decision.IsSuccess);
        Assert.Contains("broken", decision.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void OneMalformedRow_DoesNotPreventOtherRowsFromBeingNamedInError()
    {
        var rows = new[]
        {
            MakeRow("notes", NotesSpecJson),
            MakeRow("broken1", """{ "schemaVersion": 1, "name": "broken1" }"""),
            MakeRow("broken2", """{ "schemaVersion": 1, "name": "broken2" }"""),
        };

        var decision = RowAggregator.Aggregate(rows);

        Assert.False(decision.IsSuccess);
        Assert.Contains("broken1", decision.Error, StringComparison.Ordinal);
        Assert.Contains("broken2", decision.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateApplicationNames_RejectsWholeBatch()
    {
        var rows = new[]
        {
            MakeRow("notes", MakeMinimalSpecJson("notes", "NOTES")),
            MakeRow("notes-dup", MakeMinimalSpecJson("notes", "NOTES2")),
        };

        var decision = RowAggregator.Aggregate(rows);

        Assert.False(decision.IsSuccess);
        Assert.Contains("unique", decision.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownFieldInSpec_Rejected()
    {
        var row = MakeRow(
            "notes",
            """{ "schemaVersion": 1, "name": "notes", "targetAccount": "NOTES", "issuer": { "authority": "https://id.example.com", "jwksCache": "5m", "validateAudience": false }, "profiles": [], "thisFieldDoesNotExist": "typo" }""");

        var decision = RowAggregator.Aggregate([row]);

        Assert.False(decision.IsSuccess);
        Assert.Contains("thisFieldDoesNotExist", decision.Error, StringComparison.Ordinal);
    }

    private static AuthApplicationRow MakeRow(string name, string specJson)
    {
        using var doc = JsonDocument.Parse(specJson);
        return new AuthApplicationRow(name, doc.RootElement.Clone(), Enabled: true);
    }

    private static string MakeMinimalSpecJson(string name, string targetAccount) => $$"""
        {
          "schemaVersion": 1,
          "name": "{{name}}",
          "targetAccount": "{{targetAccount}}",
          "issuer": {
            "authority": "https://id.example.com/realms/{{name}}",
            "jwksCache": "5m",
            "validateAudience": false
          },
          "profiles": []
        }
        """;
}
