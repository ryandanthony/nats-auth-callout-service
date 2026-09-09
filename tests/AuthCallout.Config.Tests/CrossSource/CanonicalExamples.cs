// -----------------------------------------------------------------------
// <copyright file="CanonicalExamples.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Config.Tests.CrossSource;

/// <summary>
/// Shared golden canonical-model examples for the cross-source contract test (ADR 0007: every
/// source — files [P10], Kubernetes CRD [P11], Postgres [P12] — must map its own native shape
/// of the doc 09 <c>notes</c> example into the exact same canonical <see cref="Application"/>).
/// One shared expected-model literal here, rather than one per packet, so the three source
/// tests can never silently drift apart on what "the same example" means. Each source keeps its
/// own fixture in its own native shape (JSON/YAML file, CRD manifest, DB rows); only the
/// expected canonical output is centralized.
/// </summary>
public static class CanonicalExamples
{
    /// <summary>
    /// Gets the canonical <see cref="Application"/> every source's mapping of the doc 09
    /// <c>notes</c> example must produce, field-for-field.
    /// </summary>
    /// <value>The expected canonical model for the doc 09 <c>notes</c> example.</value>
    public static Application NotesApplication { get; } = new(
        Name: "notes",
        TargetAccount: "NOTES",
        Issuer: new IssuerConfig(
            Authority: "https://id.ants.zone/realms/main",
            JwksUri: null,
            JwksCache: DurationString.Parse("5m"),
            RefreshInterval: null,
            ValidateAudience: true,
            Audience: ["nats"],
            ClockSkew: DurationString.Parse("60s"),
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
}
