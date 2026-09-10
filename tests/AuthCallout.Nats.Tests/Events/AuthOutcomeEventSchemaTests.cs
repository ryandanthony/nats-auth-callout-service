// -----------------------------------------------------------------------
// <copyright file="AuthOutcomeEventSchemaTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Nats.Events;

namespace AuthCallout.Nats.Tests.Events;

/// <summary>
/// Contract/golden tests: every payload <see cref="AuthOutcomePayloadBuilder"/> can produce
/// validates against <c>schemas/events/auth-outcome/v1.json</c> (P09 ADR→test map: "Payload
/// validates against schemas/events/auth-outcome/v1.json for every outcome/reason
/// combination"). Serializes with the exact <see cref="JsonSerializerOptions"/>
/// <see cref="EventEmitter"/> uses internally (reflection-free: duplicated here deliberately,
/// since a public serializer-options accessor would widen <see cref="EventEmitter"/>'s surface
/// for no production benefit).
/// </summary>
public sealed class AuthOutcomeEventSchemaTests
{
    private const string SchemaPath = "events/auth-outcome/v1.json";

    private static readonly JsonSerializerOptions SerializerOptions = BuildSerializerOptions();
    private static readonly DateTimeOffset FixedTimestamp = new(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);

    public static TheoryData<DenyReason> AllDenyReasons() =>
        new(Enum.GetValues<DenyReason>());

    [Fact]
    public void GrantedAnonymous_ValidatesAgainstSchema()
    {
        var decision = Granted(application: null);
        AssertPayloadValidatesAgainstSchema(decision, userNKey: null);
    }

    [Fact]
    public void GrantedApp_ValidatesAgainstSchema()
    {
        var decision = Granted(application: "notes");
        AssertPayloadValidatesAgainstSchema(decision, userNKey: "UED123");
    }

    [Theory]
    [MemberData(nameof(AllDenyReasons))]
    public void EveryDeniedReason_ValidatesAgainstSchema(DenyReason reason)
    {
        var decision = new DeniedDecision(Application: "notes", Principal: "sub-123", Issuer: "https://id.example.com", Reason: reason);
        AssertPayloadValidatesAgainstSchema(decision, userNKey: "UED123");
    }

    [Fact]
    public void DeniedWithNoOptionalFields_ValidatesAgainstSchema()
    {
        var decision = new DeniedDecision(Application: null, Principal: null, Issuer: null, Reason: DenyReason.UnknownIssuer);
        AssertPayloadValidatesAgainstSchema(decision, userNKey: null);
    }

    [Fact]
    public void UserNKeyPresent_SerializesTheField_AndValidates()
    {
        var decision = Granted(application: "notes");
        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey: "UED123XYZ", FixedTimestamp);

        var json = JsonSerializer.SerializeToNode(payload, SerializerOptions);

        Assert.Equal("UED123XYZ", json!["userNKey"]!.GetValue<string>());
        SchemaFixture.AssertValid(SchemaPath, json);
    }

    [Fact]
    public void UserNKeyAbsent_OmitsTheFieldEntirely_AndValidates()
    {
        var decision = Granted(application: "notes");
        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey: null, FixedTimestamp);

        var json = JsonSerializer.SerializeToNode(payload, SerializerOptions);

        Assert.False(json!.AsObject().ContainsKey("userNKey"), "userNKey should be entirely absent, not present-as-null, when unavailable.");
        SchemaFixture.AssertValid(SchemaPath, json);
    }

    [Fact]
    public void Timestamp_SerializesInDoc09ExactFormat()
    {
        var decision = Granted(application: null);
        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey: null, FixedTimestamp);

        var json = JsonSerializer.SerializeToNode(payload, SerializerOptions);

        Assert.Equal("2026-09-06T22:00:00Z", json!["timestamp"]!.GetValue<string>());
    }

    [Fact]
    public void SchemaVersion_IsAlwaysOne()
    {
        var decision = Granted(application: null);
        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey: null, FixedTimestamp);

        var json = JsonSerializer.SerializeToNode(payload, SerializerOptions);

        Assert.Equal(1, json!["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public void OptionalAbsentFields_AreEntirelyOmitted_NotNull()
    {
        // Doc 09 field table: "application: Absent when no application was selected." etc.
        // Verify the JSON key itself is missing, not present with a null value, for every
        // optional field on the fully-absent anonymous-denial shape.
        var decision = new DeniedDecision(Application: null, Principal: null, Issuer: null, Reason: DenyReason.AnonymousNoContributions);
        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey: null, FixedTimestamp);

        var json = JsonSerializer.SerializeToNode(payload, SerializerOptions)!.AsObject();

        Assert.False(json.ContainsKey("application"));
        Assert.False(json.ContainsKey("profile"));
        Assert.False(json.ContainsKey("principal"));
        Assert.False(json.ContainsKey("issuer"));
        Assert.False(json.ContainsKey("userNKey"));
    }

    [Fact]
    public void DeniedDecisionDetail_NeverAppearsInSerializedPayload()
    {
        // ADR 0008 requires payloads carry "no secrets or raw tokens". Decision.Principal is
        // already documented as never-the-raw-token by its own producer's contract (doc 03/
        // GrantedDecision/DeniedDecision doc comments), so that guarantee belongs to whoever
        // constructs the Decision, not this builder. What IS this builder's own
        // responsibility: DeniedDecision.Detail — free-text, logs-only, and explicitly never
        // part of the doc-09 wire shape — must never leak onto the payload, even though the
        // builder has it available on its input.
        var detailWithSensitiveLookingText = "raw token was: eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ1c2VyIn0.c2lnbmF0dXJl";
        var decision = new DeniedDecision("notes", "user@example.com", "https://id.example.com", DenyReason.NoProfileMatch, Detail: detailWithSensitiveLookingText);

        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey: null, FixedTimestamp);
        var json = JsonSerializer.Serialize(payload, SerializerOptions);

        Assert.DoesNotContain("eyJhbGci", json, StringComparison.Ordinal);
        Assert.DoesNotContain("raw token was", json, StringComparison.Ordinal);
    }

    private static void AssertPayloadValidatesAgainstSchema(Decision decision, string? userNKey)
    {
        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey, FixedTimestamp);
        var json = JsonSerializer.SerializeToNode(payload, SerializerOptions);
        SchemaFixture.AssertValid(SchemaPath, json);
    }

    private static GrantedDecision Granted(string? application) => new(
        Application: application,
        Principal: application is null ? null : "user@example.com",
        Issuer: application is null ? null : "https://id.example.com",
        TargetAccount: "ACCT",
        Name: "user",
        Expiry: FixedTimestamp.AddMinutes(5),
        Publish: [],
        Subscribe: [],
        ProfileName: application is null ? null : "editor",
        IdentityPropagationEnabled: false,
        SelfLookupKey: null);

    private static JsonSerializerOptions BuildSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new Rfc3339DateTimeOffsetConverter());
        return options;
    }
}
