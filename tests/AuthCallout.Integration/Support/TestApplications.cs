// -----------------------------------------------------------------------
// <copyright file="TestApplications.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Integration.Support;

/// <summary>
/// Builds the <see cref="Application"/> configs the M2 scenario and its siblings exercise:
/// <c>notes</c> (role-based user profile, identity propagation enabled, <see cref="NatsServerConfigBuilder.NotesAccountName"/>)
/// and <c>tasks</c> (role-based worker profile plus a service profile matched on <c>azp</c>
/// per ADR 0002, <see cref="NatsServerConfigBuilder.TasksAccountName"/>), mirroring the
/// design docs' own two-application example (doc 08 §Local development) and
/// <c>dev/AppHost.Local/config/apps/{Note,Task}.yaml</c>'s shape, adapted to
/// <see cref="IntegrationRealm"/>'s clients/roles.
/// </summary>
public static class TestApplications
{
#pragma warning disable S2339 // Kept const (matching the repo's existing named-constant pattern, e.g. PostgresSourceSettings.DefaultNotifyChannel); these are config-fixture literal names, not mutable state.
    /// <summary>The <c>notes</c> application's name.</summary>
    public const string NotesAppName = "notes";

    /// <summary>The <c>tasks</c> application's name.</summary>
    public const string TasksAppName = "tasks";

    /// <summary>The <c>notes</c> app's editor profile name (role match).</summary>
    public const string NotesEditorProfileName = "editor";

    /// <summary>The <c>tasks</c> app's worker profile name (role match).</summary>
    public const string TasksWorkerProfileName = "worker";

    /// <summary>The <c>tasks</c> app's service profile name (<c>azp</c> match, ADR 0002).</summary>
    public const string TasksServiceProfileName = "worker-service";
#pragma warning restore S2339

#pragma warning disable S2339 // Kept const (matching the repo's existing named-constant pattern); these are wire-protocol subject literals, not mutable state.
    /// <summary>
    /// The <c>$JS.API.STREAM.INFO.&gt;</c>/<c>$JS.API.DIRECT.GET.&gt;</c> publish subjects
    /// NATS.Net's own KV client needs under the hood
    /// (<c>NatsKVContext.GetStoreAsync</c>/<c>GetEntryAsync</c>) on top of the
    /// <c>$KV.&lt;bucket&gt;.&lt;key&gt;</c> subjects ADR 0013/<c>GrantBuilder</c> already grant —
    /// not part of the KV grant contract itself, just what the wire protocol requires to look
    /// up a bucket's backing stream. The inbox reply path is NOT added here: <c>GrantBuilder</c>
    /// auto-adds the scoped per-principal inbox subject, and these tests deliberately rely on
    /// that auto-grant (plus the matching client-side <c>InboxPrefix</c>) so the scoped-inbox
    /// contract is what the suite actually exercises.
    /// </summary>
    private static readonly IReadOnlyList<string> KvClientPublishSubjects = ["$JS.API.STREAM.INFO.>", "$JS.API.DIRECT.GET.>"];
#pragma warning restore S2339

    /// <summary>Builds a <see cref="Grant"/> for a real subject list plus the standing <see cref="KvClientPublishSubjects"/> every profile needs.</summary>
    private static Grant GrantWithKvClientSubjects(IReadOnlyList<string> publish, IReadOnlyList<string> subscribe) => new(
        Publish: [.. publish, .. KvClientPublishSubjects],
        Subscribe: subscribe);

    /// <summary>Builds the <c>notes</c> application: editor profile granted <c>notes.dev.&gt;</c>, identity propagation enabled, <c>autoAddSessionAccess</c> on the editor profile.</summary>
    /// <param name="issuerAuthority">The Keycloak realm issuer authority (<see cref="Fixtures.KeycloakFixture.IssuerAuthority"/>).</param>
    /// <returns>The <c>notes</c> application config.</returns>
    public static Application NotesApp(string issuerAuthority) => new(
        Name: NotesAppName,
        TargetAccount: NatsServerConfigBuilder.NotesAccountName,
        Issuer: Issuer(issuerAuthority),
        Match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["azp"] = [IntegrationRealm.NotesClientId],
        }),
        AnonymousProfile: new Grant(Publish: ["notes.public.>"], Subscribe: ["notes.public.>"]),
        Profiles:
        [
            new Profile(
                Name: NotesEditorProfileName,
                Match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["realm_access.roles"] = ["notes-editor"],
                }),
                Grant: GrantWithKvClientSubjects(["notes.dev.>"], ["notes.dev.>"]),
                NameClaim: "preferred_username",
                Variables: [],
                PublishTemplates: [],
                SubscribeTemplates: [],
                AutoAddSessionAccess: true),
        ],
        IdentityPropagation: new IdentityPropagationConfig(Enabled: true),
        Events: new EventSubjects(Success: "notes.events.auth.success", Failure: "notes.events.auth.failure"));

    /// <summary>Builds the <c>tasks</c> application: a role-based worker profile and an <c>azp</c>-matched service profile (ADR 0002), identity propagation enabled.</summary>
    /// <param name="issuerAuthority">The Keycloak realm issuer authority.</param>
    /// <returns>The <c>tasks</c> application config.</returns>
    public static Application TasksApp(string issuerAuthority) => new(
        Name: TasksAppName,
        TargetAccount: NatsServerConfigBuilder.TasksAccountName,
        Issuer: Issuer(issuerAuthority),
        Match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["azp"] = [IntegrationRealm.TasksClientId, IntegrationRealm.ServiceClientId],
        }),
        AnonymousProfile: new Grant(Publish: ["tasks.public.>"], Subscribe: ["tasks.public.>"]),
        Profiles:
        [
            new Profile(
                Name: TasksWorkerProfileName,
                Match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["realm_access.roles"] = ["tasks-worker"],
                }),
                Grant: GrantWithKvClientSubjects(["tasks.dev.>"], ["tasks.dev.>"]),
                NameClaim: "preferred_username",
                Variables: [],
                PublishTemplates: [],
                SubscribeTemplates: [],
                AutoAddSessionAccess: true),
            new Profile(
                Name: TasksServiceProfileName,
                Match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["azp"] = [IntegrationRealm.ServiceClientId],
                }),
                Grant: GrantWithKvClientSubjects(["tasks.svc.>"], ["tasks.dev.>", "tasks.svc.>"]),
                NameClaim: "azp",
                Variables: [],
                PublishTemplates: [],
                SubscribeTemplates: [],
                AutoAddSessionAccess: true),
        ],
        IdentityPropagation: new IdentityPropagationConfig(Enabled: true),
        Events: new EventSubjects(Success: "tasks.events.auth.success", Failure: "tasks.events.auth.failure"));

    /// <summary>Builds an <see cref="IssuerConfig"/> pointed at <paramref name="issuerAuthority"/> with the pinned ADR 0016 validation parameters this suite's realm satisfies (audience <c>nats</c>, RS256).</summary>
    /// <param name="issuerAuthority">The issuer authority.</param>
    /// <returns>The issuer config.</returns>
    public static IssuerConfig Issuer(string issuerAuthority) => new(
        Authority: issuerAuthority,
        JwksUri: null,
        JwksCache: DurationString.Parse("5m"),
        RefreshInterval: null,
        ValidateAudience: true,
        Audience: ["nats"],
        ClockSkew: DurationString.Parse("60s"),
        AllowedAlgorithms: ["RS256"]);
}
