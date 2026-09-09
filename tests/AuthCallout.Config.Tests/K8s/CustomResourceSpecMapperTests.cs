// -----------------------------------------------------------------------
// <copyright file="CustomResourceSpecMapperTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Config.K8s.Mapping;
using AuthCallout.Config.Tests.CrossSource;
using AuthCallout.Contracts.Config;

namespace AuthCallout.Config.Tests.K8s;

/// <summary>
/// Unit tests for <see cref="CustomResourceSpecMapper"/> (P11 packet, doc 09 §4). The golden
/// CR fixture (<c>notes-crd.yaml</c>-equivalent spec, embedded as JSON for parsing simplicity)
/// asserts against the shared <see cref="CanonicalExamples.NotesApplication"/> (ADR 0007
/// cross-source contract with P10's file mapper).
/// </summary>
public sealed class CustomResourceSpecMapperTests
{
    /// <summary>
    /// The doc09 <c>notes</c> example's <c>spec</c> (deploy/crd/samples/Note.yaml, minus
    /// apiVersion/kind/metadata), expressed as JSON for direct <see cref="JsonElement"/> parsing.
    /// </summary>
    private const string NotesSpecJson = """
        {
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
    public void Doc09CrdSpecExample_MapsToExpectedCanonicalModel()
    {
        var spec = Parse(NotesSpecJson);

        var result = CustomResourceSpecMapper.TryMap("notes", spec);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(CanonicalExamples.NotesApplication, result.Application);
    }

    [Fact]
    public void ApplicationNameFollowsMetadataName_NotAnySpecField()
    {
        var spec = Parse(NotesSpecJson);

        var result = CustomResourceSpecMapper.TryMap("a-completely-different-name", spec);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("a-completely-different-name", result.Application!.Name);
    }

    [Fact]
    public void UnknownTopLevelSpecField_Rejected()
    {
        var spec = ParseWithMutation(NotesSpecJson, obj => obj["thisFieldDoesNotExist"] = "typo");

        var result = CustomResourceSpecMapper.TryMap("notes", spec);

        Assert.False(result.IsSuccess);
        Assert.Contains("thisFieldDoesNotExist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownNestedIssuerField_Rejected()
    {
        var spec = ParseWithMutation(
            NotesSpecJson, obj => ((System.Text.Json.Nodes.JsonObject)obj["issuer"]!)["notARealField"] = "typo");

        var result = CustomResourceSpecMapper.TryMap("notes", spec);

        Assert.False(result.IsSuccess);
        Assert.Contains("notARealField", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRequiredField_Rejected()
    {
        var spec = ParseWithMutation(NotesSpecJson, obj => obj.Remove("targetAccount"));

        var result = CustomResourceSpecMapper.TryMap("notes", spec);

        Assert.False(result.IsSuccess);
        Assert.Contains("targetAccount", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SpecNotAnObject_Rejected()
    {
        using var doc = JsonDocument.Parse("[1,2,3]");

        var result = CustomResourceSpecMapper.TryMap("notes", doc.RootElement);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void MinimalSpec_NoOptionalFields_MapsSuccessfully()
    {
        const string minimal = """
            {
              "targetAccount": "MIN",
              "issuer": {
                "authority": "https://id.example.com",
                "jwksCache": "5m",
                "validateAudience": false
              },
              "profiles": []
            }
            """;

        var result = CustomResourceSpecMapper.TryMap("minimal", Parse(minimal));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("minimal", result.Application!.Name);
        Assert.Null(result.Application.Match);
        Assert.Null(result.Application.AnonymousProfile);
        Assert.Empty(result.Application.Profiles);
        Assert.Equal(IssuerConfig.DefaultAllowedAlgorithms, result.Application.Issuer.AllowedAlgorithms);
    }

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement ParseWithMutation(string json, Action<System.Text.Json.Nodes.JsonObject> mutate)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        mutate(node);
        return JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
    }
}
