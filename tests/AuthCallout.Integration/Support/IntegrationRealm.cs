// -----------------------------------------------------------------------
// <copyright file="IntegrationRealm.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Integration.Support;

/// <summary>
/// The Keycloak realm this suite imports into its own <c>KeycloakFixture</c> (deliberately
/// separate from <c>dev/AppHost.Local/realm/dev-realm.json</c>, which P15 owns — this suite
/// owns only <c>tests/AuthCallout.Integration/**</c>, per its packet's file-ownership rule).
/// Mirrors the dev realm's shape (two apps, password-grant web clients, role claims) but adds
/// what the deny-matrix/service-principal/TTL scenarios need: a client-credentials service
/// client (<see cref="ServiceClientId"/>, ADR 0002 §<c>azp</c>), a wrong-audience client
/// (<see cref="WrongAudienceClientId"/>, no <c>nats-audience</c> mapper), a short-lived-token
/// client (<see cref="ShortLivedClientId"/>, scenario 6's per-message TTL proof), and a
/// no-role user (<see cref="NoRoleUsername"/>, the no-profile-match deny case).
/// </summary>
public static class IntegrationRealm
{
#pragma warning disable S2339 // Kept const (matching the repo's existing named-constant pattern, e.g. PostgresSourceSettings.DefaultNotifyChannel); these are realm-fixture literal names, not mutable state.
    /// <summary>Gets the realm name.</summary>
    public const string RealmName = "integration";

    /// <summary>Gets the <c>notes</c> application's web client id (password grant, <c>notes-editor</c> role).</summary>
    public const string NotesClientId = "notes-web";

    /// <summary>Gets the <c>tasks</c> application's web client id (password grant, <c>tasks-worker</c> role).</summary>
    public const string TasksClientId = "tasks-web";

    /// <summary>Gets the service (client-credentials) client id matched by the <c>tasks</c> application's <c>worker-service</c> profile via <c>azp</c> (ADR 0002).</summary>
    public const string ServiceClientId = "tasks-service";

    /// <summary>Gets a client with no <c>nats-audience</c> protocol mapper, for the wrong-audience deny scenario.</summary>
    public const string WrongAudienceClientId = "wrong-audience-web";

    /// <summary>Gets a client whose issued tokens live only a few seconds, for the per-message TTL scenario.</summary>
    public const string ShortLivedClientId = "short-lived-web";

    /// <summary>Gets the username of the <c>notes-editor</c> role holder.</summary>
    public const string NoteUsername = "note-user";

    /// <summary>Gets the password for <see cref="NoteUsername"/>.</summary>
    public const string NotePassword = "note-pass";

    /// <summary>Gets the username of the <c>tasks-worker</c> role holder.</summary>
    public const string TaskUsername = "task-user";

    /// <summary>Gets the password for <see cref="TaskUsername"/>.</summary>
    public const string TaskPassword = "task-pass";

    /// <summary>Gets the username of a user with no realm roles at all, for the no-profile-match deny case.</summary>
    public const string NoRoleUsername = "no-role-user";

    /// <summary>Gets the password for <see cref="NoRoleUsername"/>.</summary>
    public const string NoRolePassword = "no-role-pass";

    /// <summary>Gets the client-credentials secret for <see cref="ServiceClientId"/>.</summary>
    public const string ServiceClientSecret = "tasks-service-secret";

    /// <summary>Gets the seconds a <see cref="ShortLivedClientId"/> token remains valid for.</summary>
    public const int ShortLivedTokenSeconds = 6;
#pragma warning restore S2339

    /// <summary>Builds the realm export JSON imported by <c>Testcontainers.Keycloak</c>'s <c>WithRealm</c>.</summary>
    /// <returns>The realm export document.</returns>
    public static string BuildRealmJson()
    {
        return $$"""
        {
          "realm": "{{RealmName}}",
          "accessTokenLifespan": 300,
          "enabled": true,
          "sslRequired": "none",
          "registrationAllowed": false,
          "roles": {
            "realm": [
              { "name": "notes-editor", "composite": false, "clientRole": false, "attributes": {} },
              { "name": "tasks-worker", "composite": false, "clientRole": false, "attributes": {} }
            ]
          },
          "clients": [
            {{PasswordGrantClient(NotesClientId)}},
            {{PasswordGrantClient(TasksClientId)}},
            {{WrongAudienceClient()}},
            {{ShortLivedClient()}},
            {{ServiceCredentialsClient()}}
          ],
          "users": [
            {{User(NoteUsername, NotePassword, ["notes-editor"])}},
            {{User(TaskUsername, TaskPassword, ["tasks-worker"])}},
            {{User(NoRoleUsername, NoRolePassword, [])}}
          ]
        }
        """;
    }

    private static string AudienceMapper() => """
        {
          "name": "nats-audience",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-audience-mapper",
          "consentRequired": false,
          "config": {
            "id.token.claim": "false",
            "included.custom.audience": "nats",
            "access.token.claim": "true"
          }
        }
        """;

    private static string PasswordGrantClient(string clientId) => $$"""
        {
          "clientId": "{{clientId}}",
          "enabled": true,
          "redirectUris": [],
          "webOrigins": [],
          "standardFlowEnabled": false,
          "implicitFlowEnabled": false,
          "directAccessGrantsEnabled": true,
          "serviceAccountsEnabled": false,
          "publicClient": true,
          "protocol": "openid-connect",
          "protocolMappers": [ {{AudienceMapper()}} ]
        }
        """;

    private static string WrongAudienceClient() => $$"""
        {
          "clientId": "{{WrongAudienceClientId}}",
          "enabled": true,
          "redirectUris": [],
          "webOrigins": [],
          "standardFlowEnabled": false,
          "implicitFlowEnabled": false,
          "directAccessGrantsEnabled": true,
          "serviceAccountsEnabled": false,
          "publicClient": true,
          "protocol": "openid-connect",
          "protocolMappers": []
        }
        """;

    private static string ShortLivedClient() => $$"""
        {
          "clientId": "{{ShortLivedClientId}}",
          "enabled": true,
          "redirectUris": [],
          "webOrigins": [],
          "standardFlowEnabled": false,
          "implicitFlowEnabled": false,
          "directAccessGrantsEnabled": true,
          "serviceAccountsEnabled": false,
          "publicClient": true,
          "protocol": "openid-connect",
          "attributes": { "access.token.lifespan": "{{ShortLivedTokenSeconds}}" },
          "protocolMappers": [ {{AudienceMapper()}} ]
        }
        """;

    private static string ServiceCredentialsClient() => $$"""
        {
          "clientId": "{{ServiceClientId}}",
          "enabled": true,
          "redirectUris": [],
          "webOrigins": [],
          "standardFlowEnabled": false,
          "implicitFlowEnabled": false,
          "directAccessGrantsEnabled": false,
          "serviceAccountsEnabled": true,
          "publicClient": false,
          "secret": "{{ServiceClientSecret}}",
          "protocol": "openid-connect",
          "protocolMappers": [ {{AudienceMapper()}} ]
        }
        """;

    private static string User(string username, string password, IReadOnlyList<string> realmRoles)
    {
        var roles = string.Join(", ", realmRoles.Select(static r => $"\"{r}\""));
        return $$"""
        {
          "username": "{{username}}",
          "enabled": true,
          "emailVerified": true,
          "firstName": "Integration",
          "lastName": "Test",
          "email": "{{username}}@example.com",
          "requiredActions": [],
          "credentials": [ { "type": "password", "value": "{{password}}", "temporary": false } ],
          "realmRoles": [ {{roles}} ]
        }
        """;
    }
}
