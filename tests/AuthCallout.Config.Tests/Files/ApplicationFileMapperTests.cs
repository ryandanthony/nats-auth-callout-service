// -----------------------------------------------------------------------
// <copyright file="ApplicationFileMapperTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Config.Files.Parsing;
using AuthCallout.Config.Files.Yaml;
using AuthCallout.Config.Tests.CrossSource;
using AuthCallout.Contracts.Config;

namespace AuthCallout.Config.Tests.Files;

/// <summary>
/// Unit tests for <see cref="ApplicationFileMapper"/> (P10 packet, doc 09 §3 rule table).
/// Golden-fixture and round-trip coverage against the shared cross-source expected model
/// (<see cref="CanonicalExamples.NotesApplication"/>, ADR 0007); directory/watch behavior
/// lives in <see cref="FileRoutingConfigProviderTests"/>.
/// </summary>
public sealed class ApplicationFileMapperTests
{
    [Fact]
    public void Doc09JsonExample_MapsToExpectedCanonicalModel()
    {
        var json = ReadFixture("application-notes.json");
        using var doc = JsonDocument.Parse(json);

        var result = ApplicationFileMapper.TryMap(doc.RootElement);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(CanonicalExamples.NotesApplication, result.Application);
        Assert.False(result.SchemaVersionDefaulted);
    }

    [Fact]
    public void Doc09YamlExample_MapsToExpectedCanonicalModel()
    {
        var yaml = ReadFixture("application-notes.yaml");
        var root = YamlToJsonConverter.Convert(yaml);

        var result = ApplicationFileMapper.TryMap(root);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(CanonicalExamples.NotesApplication, result.Application);
    }

    [Fact]
    public void YamlAndJsonSameShape_ProduceIdenticalModel()
    {
        var jsonResult = ApplicationFileMapper.TryMap(JsonDocument.Parse(ReadFixture("application-notes.json")).RootElement);
        var yamlResult = ApplicationFileMapper.TryMap(YamlToJsonConverter.Convert(ReadFixture("application-notes.yaml")));

        Assert.True(jsonResult.IsSuccess);
        Assert.True(yamlResult.IsSuccess);
        Assert.Equal(jsonResult.Application, yamlResult.Application);
    }

    [Fact]
    public void UnknownTopLevelField_Rejected()
    {
        var root = ParseWithMutation(ReadFixture("application-notes.json"), obj => obj["thisFieldDoesNotExist"] = "typo");

        var result = ApplicationFileMapper.TryMap(root);

        Assert.False(result.IsSuccess);
        Assert.Contains("thisFieldDoesNotExist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownNestedIssuerField_Rejected()
    {
        var root = ParseWithMutation(
            ReadFixture("application-notes.json"),
            obj => ((System.Text.Json.Nodes.JsonObject)obj["issuer"]!)["notARealField"] = "typo");

        var result = ApplicationFileMapper.TryMap(root);

        Assert.False(result.IsSuccess);
        Assert.Contains("notARealField", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownProfileField_Rejected()
    {
        var root = ParseWithMutation(
            ReadFixture("application-notes.json"),
            obj => ((System.Text.Json.Nodes.JsonObject)obj["profiles"]![0]!)["notAField"] = "typo");

        var result = ApplicationFileMapper.TryMap(root);

        Assert.False(result.IsSuccess);
        Assert.Contains("notAField", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaVersionMissing_TreatedAsOneWithDefaultedFlag()
    {
        var root = ParseWithMutation(ReadFixture("application-notes.json"), obj => obj.Remove("schemaVersion"));

        var result = ApplicationFileMapper.TryMap(root);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.SchemaVersionDefaulted);
    }

    [Fact]
    public void SchemaVersionSupported_NotDefaultedAndSucceeds()
    {
        var root = ParseWithMutation(ReadFixture("application-notes.json"), obj => obj["schemaVersion"] = 1);

        var result = ApplicationFileMapper.TryMap(root);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.SchemaVersionDefaulted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void SchemaVersionUnsupported_Rejected(int unsupportedVersion)
    {
        var root = ParseWithMutation(ReadFixture("application-notes.json"), obj => obj["schemaVersion"] = unsupportedVersion);

        var result = ApplicationFileMapper.TryMap(root);

        Assert.False(result.IsSuccess);
        Assert.Contains("schemaVersion", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRequiredField_Rejected()
    {
        var root = ParseWithMutation(ReadFixture("application-notes.json"), obj => obj.Remove("targetAccount"));

        var result = ApplicationFileMapper.TryMap(root);

        Assert.False(result.IsSuccess);
        Assert.Contains("targetAccount", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RootNotAnObject_Rejected()
    {
        using var doc = JsonDocument.Parse("[1,2,3]");

        var result = ApplicationFileMapper.TryMap(doc.RootElement);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void MinimalApplication_NoOptionalFields_MapsSuccessfully()
    {
        const string minimal = """
            {
              "schemaVersion": 1,
              "name": "minimal",
              "targetAccount": "MIN",
              "issuer": {
                "authority": "https://id.example.com",
                "jwksCache": "5m",
                "validateAudience": false
              },
              "profiles": []
            }
            """;
        using var doc = JsonDocument.Parse(minimal);

        var result = ApplicationFileMapper.TryMap(doc.RootElement);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("minimal", result.Application!.Name);
        Assert.Null(result.Application.Match);
        Assert.Null(result.Application.AnonymousProfile);
        Assert.Null(result.Application.IdentityPropagation);
        Assert.Null(result.Application.Events);
        Assert.Empty(result.Application.Profiles);
        Assert.Equal(IssuerConfig.DefaultAllowedAlgorithms, result.Application.Issuer.AllowedAlgorithms);
        Assert.Equal(IssuerConfig.DefaultClockSkew, result.Application.Issuer.ClockSkew);
    }

    [Fact]
    public void InvalidTransform_IsReportedByTransformParser()
    {
        const string withBadTransform = """
            {
              "schemaVersion": 1,
              "name": "app",
              "targetAccount": "APP",
              "issuer": { "authority": "https://id.example.com", "jwksCache": "5m", "validateAudience": false },
              "profiles": [
                {
                  "name": "p",
                  "match": {},
                  "grant": { "publish": [], "subscribe": [] },
                  "nameClaim": "sub",
                  "variables": [
                    { "name": "v", "claim": "c", "transforms": ["not-a-real-transform"] }
                  ]
                }
              ]
            }
            """;
        using var doc = JsonDocument.Parse(withBadTransform);

        var result = ApplicationFileMapper.TryMap(doc.RootElement);

        Assert.False(result.IsSuccess);
        Assert.Contains("not-a-real-transform", result.Error, StringComparison.Ordinal);
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Files", "Fixtures", name));

    private static JsonElement ParseWithMutation(string json, Action<System.Text.Json.Nodes.JsonObject> mutate)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        mutate(node);
        return JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
    }
}
