// -----------------------------------------------------------------------
// <copyright file="ApplicationFileRoundTripTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using AuthCallout.Contracts.Config;

namespace AuthCallout.Contracts.Tests.Schemas;

/// <summary>
/// Round-trip test (P02 DoD): the doc 09 example application file deserializes into the
/// canonical model and matches a hand-built expected instance field-for-field. This is a
/// minimal, local mapping — not P10's production deserializer — that exists solely to prove
/// the canonical model can faithfully represent doc 09's example without lossy translation.
/// </summary>
public sealed class ApplicationFileRoundTripTests
{
    [Fact]
    public void Doc09ApplicationExample_MapsToExpectedCanonicalModel()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "config", "application-notes.json"));
        var node = JsonNode.Parse(json)!.AsObject();

        var actual = MapToApplication(node);

        var expected = new Application(
            Name: "notes",
            TargetAccount: "NOTES",
            Issuer: new IssuerConfig(
                Authority: "https://id.ants.zone/realms/main",
                JwksUri: null,
                JwksCache: Primitives.DurationString.Parse("5m"),
                RefreshInterval: null,
                ValidateAudience: true,
                Audience: ["nats"],
                ClockSkew: Primitives.DurationString.Parse("60s"),
                AllowedAlgorithms: ["RS256", "ES256"]),
            Match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["azp"] = ["notes-web"] }),
            AnonymousProfile: new Grant(Publish: [], Subscribe: ["notes.public.>"]),
            Profiles:
            [
                new Profile(
                    Name: "editor",
                    Match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["realm_access.roles"] = ["notes-editor"] }),
                    Grant: new Grant(Publish: ["notes.{tenant}.>"], Subscribe: ["notes.{tenant}.>"]),
                    NameClaim: "sub",
                    Variables: [new TemplateVariable("tenant", "tenant_id", [Transform.Lowercase, Transform.Sanitize], Required: true)],
                    PublishTemplates: ["notes.{tenant}.outbox.>"],
                    SubscribeTemplates: ["notes.{tenant}.inbox.>"],
                    AutoAddSessionAccess: false),
            ],
            IdentityPropagation: new IdentityPropagationConfig(Enabled: true),
            Events: new EventSubjects(Success: "notes.events.auth.success", Failure: "notes.events.auth.failure"));

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// A minimal doc-09-shape-to-canonical-model mapper, scoped to what this test's fixture
    /// exercises. Production file mapping (including strict unknown-field rejection and the
    /// full schemaVersion migration-window handling) is P10's <c>FileRoutingProvider</c>.
    /// </summary>
    private static Application MapToApplication(JsonObject root)
    {
        var issuerNode = root["issuer"]!.AsObject();
        var issuer = new IssuerConfig(
            Authority: (string)issuerNode["authority"]!,
            JwksUri: (string?)issuerNode["jwksUri"],
            JwksCache: Primitives.DurationString.Parse((string)issuerNode["jwksCache"]!),
            RefreshInterval: (string?)issuerNode["refreshInterval"] is { } ri ? Primitives.DurationString.Parse(ri) : null,
            ValidateAudience: (bool)issuerNode["validateAudience"]!,
            Audience: issuerNode["audience"]?.AsArray().Select(n => (string)n!).ToArray(),
            ClockSkew: Primitives.DurationString.Parse((string)issuerNode["clockSkew"]!),
            AllowedAlgorithms: issuerNode["allowedAlgorithms"]!.AsArray().Select(n => (string)n!).ToArray());

        var match = MapClaimMatch(root["match"]?.AsObject());
        var anonymousProfile = root["anonymousProfile"] is { } anon ? MapGrant(anon.AsObject()) : null;
        var identityPropagation = root["identityPropagation"] is { } ip
            ? new IdentityPropagationConfig((bool)ip["enabled"]!)
            : null;
        var events = root["events"] is { } ev
            ? new EventSubjects((string?)ev["success"], (string?)ev["failure"])
            : null;

        var profiles = root["profiles"]!.AsArray().Select(p => MapProfile(p!.AsObject())).ToArray();

        return new Application(
            Name: (string)root["name"]!,
            TargetAccount: (string)root["targetAccount"]!,
            Issuer: issuer,
            Match: match,
            AnonymousProfile: anonymousProfile,
            Profiles: profiles,
            IdentityPropagation: identityPropagation,
            Events: events);
    }

    private static Profile MapProfile(JsonObject node)
    {
        var variables = node["variables"]?.AsArray().Select(v => MapVariable(v!.AsObject())).ToArray() ?? [];
        return new Profile(
            Name: (string)node["name"]!,
            Match: MapClaimMatch(node["match"]!.AsObject())!,
            Grant: MapGrant(node["grant"]!.AsObject()),
            NameClaim: (string)node["nameClaim"]!,
            Variables: variables,
            PublishTemplates: node["publishTemplates"]?.AsArray().Select(n => (string)n!).ToArray() ?? [],
            SubscribeTemplates: node["subscribeTemplates"]?.AsArray().Select(n => (string)n!).ToArray() ?? [],
            AutoAddSessionAccess: (bool?)node["autoAddSessionAccess"] ?? false);
    }

    private static TemplateVariable MapVariable(JsonObject node)
    {
        var transforms = node["transforms"]?.AsArray().Select(MapTransform).ToArray() ?? [];
        return new TemplateVariable(
            Name: (string)node["name"]!,
            Claim: (string)node["claim"]!,
            Transforms: transforms,
            Required: (bool?)node["required"] ?? false);
    }

    private static Transform MapTransform(JsonNode? node)
    {
        var element = JsonSerializer.Deserialize<JsonElement>(node!.ToJsonString());
        return Transform.Parse(element);
    }

    private static Grant MapGrant(JsonObject node) => new(
        Publish: node["publish"]?.AsArray().Select(n => (string)n!).ToArray() ?? [],
        Subscribe: node["subscribe"]?.AsArray().Select(n => (string)n!).ToArray() ?? []);

    private static ClaimMatch? MapClaimMatch(JsonObject? node)
    {
        if (node is null)
        {
            return null;
        }

        var predicates = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (key, value) in node)
        {
            predicates[key] = value!.AsArray().Select(n => (string)n!).ToArray();
        }

        return new ClaimMatch(predicates);
    }
}
