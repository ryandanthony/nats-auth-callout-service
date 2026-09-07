// -----------------------------------------------------------------------
// <copyright file="SchemaFixtureTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace AuthCallout.Contracts.Tests.Schemas;

/// <summary>
/// Validates doc 09's example payloads (embedded as fixtures under
/// <c>Fixtures/</c>) against the hand-authored JSON Schemas. Drift between the docs and the
/// schemas fails here, per the P02 packet's DoD.
/// </summary>
public sealed class SchemaFixtureTests
{
    [Fact]
    public void Doc09ApplicationExample_ValidatesAgainstApplicationSchema()
    {
        var instance = LoadFixture("config/application-notes.json");

        SchemaFixture.AssertValid("config/application/v1.json", instance);
    }

    [Fact]
    public void Doc09BootstrapExample_ValidatesAgainstBootstrapSchema()
    {
        var instance = LoadFixture("config/bootstrap.json");

        SchemaFixture.AssertValid("config/bootstrap/v1.json", instance);
    }

    [Fact]
    public void Doc09AuthOutcomeSuccessExample_ValidatesAgainstEventSchema()
    {
        var instance = LoadFixture("events/auth-outcome-success.json");

        SchemaFixture.AssertValid("events/auth-outcome/v1.json", instance);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("invalid_token")]
    [InlineData("no_issuer_match")]
    [InlineData("no_app_match")]
    [InlineData("ambiguous_app")]
    [InlineData("no_profile_match")]
    [InlineData("grant_construction_failed")]
    [InlineData("granted")]
    public void EveryDoc09ReasonCode_ValidatesAgainstEventSchema(string reasonCode)
    {
        var outcome = string.Equals(reasonCode, "granted", StringComparison.Ordinal) || string.Equals(reasonCode, "anonymous", StringComparison.Ordinal)
            ? "success"
            : "failure";
        var instance = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["timestamp"] = "2026-09-06T22:00:00Z",
            ["outcome"] = outcome,
            ["reason"] = reasonCode,
        };

        SchemaFixture.AssertValid("events/auth-outcome/v1.json", instance);
    }

    [Fact]
    public void EventSchema_AdditionalPropertiesAllowed_ForForwardCompatibility()
    {
        var instance = LoadFixture("events/auth-outcome-success.json")!.AsObject();
        instance["someFutureField"] = "future-value";

        SchemaFixture.AssertValid("events/auth-outcome/v1.json", instance);
    }

    [Fact]
    public void EventSchema_MissingRequiredField_Rejected()
    {
        var instance = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["timestamp"] = "2026-09-06T22:00:00Z",

            // "outcome" and "reason" deliberately omitted.
        };

        SchemaFixture.AssertInvalid("events/auth-outcome/v1.json", instance);
    }

    [Fact]
    public void Doc09DiscoveryEnabledExample_ValidatesAgainstDiscoverySchema()
    {
        var instance = LoadFixture("discovery/descriptor-enabled.json");

        SchemaFixture.AssertValid("discovery/descriptor/v1.json", instance);
    }

    [Fact]
    public void Doc09DiscoveryDisabledExample_ValidatesAgainstDiscoverySchema()
    {
        var instance = LoadFixture("discovery/descriptor-disabled.json");

        SchemaFixture.AssertValid("discovery/descriptor/v1.json", instance);
    }

    [Fact]
    public void DiscoverySchema_EnabledTrue_MissingBucketFields_Rejected()
    {
        var instance = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["app"] = "notes",
            ["identityPropagation"] = new JsonObject { ["enabled"] = true },
        };

        SchemaFixture.AssertInvalid("discovery/descriptor/v1.json", instance);
    }

    [Fact]
    public void DiscoverySchema_AdditionalPropertiesAllowed_ForForwardCompatibility()
    {
        var instance = LoadFixture("discovery/descriptor-disabled.json")!.AsObject();
        instance["someFutureField"] = "future-value";

        SchemaFixture.AssertValid("discovery/descriptor/v1.json", instance);
    }

    [Fact]
    public void ApplicationSchema_UnknownTopLevelField_Rejected()
    {
        var instance = LoadFixture("config/application-notes.json")!.AsObject();
        instance["thisFieldDoesNotExist"] = "typo";

        SchemaFixture.AssertInvalid("config/application/v1.json", instance);
    }

    [Fact]
    public void ApplicationSchema_MissingRequiredField_Rejected()
    {
        var instance = LoadFixture("config/application-notes.json")!.AsObject();
        instance.Remove("targetAccount");

        SchemaFixture.AssertInvalid("config/application/v1.json", instance);
    }

    [Fact]
    public void ApplicationSchema_DisallowedAlgorithm_Rejected()
    {
        var instance = LoadFixture("config/application-notes.json")!.AsObject();
        ((JsonObject)instance["issuer"]!)["allowedAlgorithms"] = new JsonArray("HS256");

        SchemaFixture.AssertInvalid("config/application/v1.json", instance);
    }

    [Fact]
    public void BootstrapSchema_UnknownTopLevelField_Rejected()
    {
        var instance = LoadFixture("config/bootstrap.json")!.AsObject();
        instance["thisFieldDoesNotExist"] = "typo";

        SchemaFixture.AssertInvalid("config/bootstrap/v1.json", instance);
    }

    [Fact]
    public void BootstrapSchema_XkeySeed_Accepted()
    {
        var instance = LoadFixture("config/bootstrap.json")!.AsObject();
        instance["xkeySeed"] = new JsonObject { ["kind"] = "file", ["path"] = "/secrets/xkey.seed" };

        SchemaFixture.AssertValid("config/bootstrap/v1.json", instance);
    }

    [Fact]
    public void BootstrapSchema_PostgresSource_RequiresConnectionString()
    {
        var instance = LoadFixture("config/bootstrap.json")!.AsObject();
        instance["source"] = new JsonObject
        {
            ["kind"] = "postgres",
            ["warnStaleness"] = "60s",
            ["maxStaleness"] = "15m",
            ["reconnectBackoff"] = "5s",
        };

        SchemaFixture.AssertInvalid("config/bootstrap/v1.json", instance);
    }

    [Fact]
    public void BootstrapSchema_PostgresSource_WithConnectionString_Accepted()
    {
        var instance = LoadFixture("config/bootstrap.json")!.AsObject();
        instance["source"] = new JsonObject
        {
            ["kind"] = "postgres",
            ["connectionString"] = new JsonObject { ["kind"] = "inline", ["value"] = "postgres://localhost/db" },
            ["pollInterval"] = "30s",
            ["notifyChannel"] = "auth_applications_changed",
            ["warnStaleness"] = "60s",
            ["maxStaleness"] = "15m",
            ["reconnectBackoff"] = "5s",
        };

        SchemaFixture.AssertValid("config/bootstrap/v1.json", instance);
    }

    [Fact]
    public void BootstrapSchema_AssertionKeyWithReservedKid_Rejected()
    {
        var instance = LoadFixture("config/bootstrap.json")!.AsObject();
        ((JsonArray)instance["assertionKeys"]!)[0]!["kid"] = "current";

        SchemaFixture.AssertInvalid("config/bootstrap/v1.json", instance);
    }

    private static JsonNode? LoadFixture(string relativePath)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath.Replace('/', Path.DirectorySeparatorChar));
        var json = File.ReadAllText(fullPath);
        return JsonNode.Parse(json);
    }
}
