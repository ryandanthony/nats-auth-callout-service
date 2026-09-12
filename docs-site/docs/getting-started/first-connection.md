---
sidebar_position: 4
---

# Your first connection

This walks through what happens on a real connection, using the
[`NoteService` sample](https://github.com/ryandanthony/nats-auth-callout-service/tree/main/dev/samples/NoteService)
as the reference client (`dotnet run --project dev/AppHost.Local` runs it
for you, plus a Keycloak realm and NATS).

## 1. Get an OIDC access token

The client authenticates against the identity provider first — the callout
never sees a username/password, only the resulting bearer token:

```csharp
var token = await DevOidcTokenFetcher.FetchAccessTokenAsync(
    keycloakBaseUrl, realm: "dev", clientId: "notes-web",
    username: "note-user", password: "note-pass", CancellationToken.None);
```

## 2. Connect with the token as the NATS auth token

```csharp
await using var client = new NatsClient(new NatsOpts
{
    Url = natsUrl,
    AuthOpts = new NatsAuthOpts { Token = token },

    // The callout grants a scoped reply inbox (_INBOX.{app-slug}.{principal}.>),
    // not the account-wide _INBOX.>; the connection must use the matching
    // prefix or KV reads/request-replies silently time out.
    InboxPrefix = CalloutInboxPrefix.For("notes", token),
});
await client.ConnectAsync();
```

The NATS client library sends the bearer token as `connect_opts.auth_token`.
The server forwards the whole connect request to the callout via
`$SYS.REQ.USER.AUTH`; the callout validates the token against the `notes`
application's issuer JWKS, matches it to the `editor` profile (via
`realm_access.roles: [notes-editor]` in
[`Note.yaml`](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/dev/AppHost.Local/config/apps/Note.yaml)),
and mints a NATS user JWT scoped into the `NOTES` account with the grant
subjects.

## 3. Discover whether identity propagation is enabled

```csharp
var kv = client.CreateKeyValueStoreContext();
var descriptor = await CalloutDiscovery.FetchAsync(kv, "notes");
// descriptor.IdentityPropagation.Enabled == true
```

## 4. Fetch your own signed assertion (once) and attach it to publishes

```csharp
var assertion = descriptor.IdentityPropagation.Enabled
    ? await CalloutSelfAssertion.FetchAsync(kv, descriptor, token)
    : null;

var headers = new NatsHeaders { { descriptor.IdentityPropagation.Header, assertion } };
await client.Connection.PublishAsync("notes.hello", "note #1", headers: headers);
```

That's the full happy path: OIDC token in, scoped NATS grant + a verifiable
identity header out. (This page uses the raw NATS.NET client to show the
callout flow directly; the `dev/samples` `NoteService`/`TaskService` do the
same thing on top of [Wolverine](https://wolverinefx.net), attaching the
identity header via a message middleware — see [`dev/README.md`](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/dev/README.md).)
See [Identity propagation](../guides/identity-propagation)
for how a subscriber verifies that header and resolves your OIDC claims, and
[Authorization model](../guides/authorization-model) for how profiles,
`match`, and template variables decide the grant.
