---
sidebar_position: 7
---

# Client library guide

`AuthCallout.Client` is the supported way for a .NET application — a user-facing
client or a backend service — to connect to NATS through the callout without
hand-assembling token handling, the scoped inbox prefix, retry semantics, or
identity propagation. Grounds: [ADR 0020](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0020-ship-client-integration-libraries.md),
[ADR 0021](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0021-client-connection-retry-semantics.md).

```bash
dotnet add package AuthCallout.Client
```

For a Wolverine host, use [`AuthCallout.Client.Wolverine`](./wolverine.md) instead,
which layers on this package.

## What it gives you

| Concern | Type |
|---|---|
| Token acquisition + proactive refresh | `ICalloutTokenProvider` (`ClientCredentialsTokenProvider`, `StaticTokenProvider`, `DelegateTokenProvider`) |
| Correct `NatsOpts` (bearer token + scoped inbox prefix) | `CalloutNatsOpts.CreateAsync` |
| Managed connection with the ADR 0021 retry contract | `CalloutNatsConnection` |
| Discovery, self-assertion, `Callout-Identity` attach | `CalloutDiscovery`, `CalloutSelfAssertion`, `CalloutIdentityHeader` |
| Service-side assertion verification | `CalloutIdentityResolver`, `ICalloutIdentityVerifier`, `CalloutIdentityContext` |
| DI wiring | `services.AddCalloutNatsClient(...)` |

## Connect as a service (client credentials)

A service is just another OIDC principal
([ADR 0002](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0002-service-auth-via-oidc-not-known-keys.md)):
it authenticates with a client-credentials JWT and gets a named profile matched
on `azp` (or `client_id`). No NKey, no `auth_users` entry in the NATS config.

```csharp
using AuthCallout.Client.Connection;
using AuthCallout.Client.Tokens;

var tokenProvider = new ClientCredentialsTokenProvider(new ClientCredentialsTokenProviderOptions
{
    Authority = new Uri("https://id.example.com/realms/prod"), // or TokenEndpoint directly
    ClientId = "orders-service",
    ClientSecret = SecretRef.FromFile("/secrets/orders-client-secret"),  // or SecretRef.Inline(...) for dev
});

await using var connection = new CalloutNatsConnection(new CalloutClientOptions
{
    Url = "nats://nats:4222",
    ApplicationName = "orders",      // the NACS application name; slugified internally
    TokenProvider = tokenProvider,
    Name = "orders-service",
});

await connection.ConnectAsync();
// connection.Connection is the live NATS.Net NatsConnection
```

The provider re-mints the token ~30 seconds before `exp`; on a failed re-mint it
keeps serving the stale token and backs off 5 seconds rather than hammering the
IdP.

## Connect as a user client

If your app has its own auth stack (browser login, device flow, token cache),
wrap it in a `DelegateTokenProvider`:

```csharp
var tokenProvider = new DelegateTokenProvider(ct => myAuthService.GetAccessTokenAsync(ct));
```

Returning `null` from the delegate makes the authenticated connect fail with
`CalloutAuthenticationRequiredException` — it never silently downgrades to an
anonymous connection. Anonymous is an explicit opt-in:

```csharp
await using var anon = new CalloutNatsConnection(new CalloutClientOptions
{
    Url = "nats://nats:4222",
    ApplicationName = "orders",
    Anonymous = true,                 // inbox prefix defaults to "{slug}._INBOX"
});
await anon.ConnectAnonymousAsync();
```

## Retry and failure semantics (ADR 0021)

`CalloutNatsConnection` implements a fixed contract:

1. **Transport failures** (server down, network): retry indefinitely with
   exponential backoff and jitter.
2. **`Authorization Violation`** (the callout denied the token): invalidate the
   cached token, force a refresh, retry **exactly once**, then fail terminally.
   A bad credential fails in seconds, not minutes.
3. **No token available**: immediate `CalloutAuthenticationRequiredException`.
4. **Reconnects are owned by the library** (`MaxReconnectRetry = 0` underneath):
   every reconnect fetches a current token and recomputes the scoped inbox
   prefix.

Observe `State` / `StateChanged`
(`Disconnected`/`Connecting`/`Connected`/`Retrying`/`Failed`) to surface
"re-login needed" to users.

## The scoped inbox prefix

The callout grants each authenticated connection only its own inbox,
`_INBOX.{app-slug}.{principal}.>`, never the account-wide `_INBOX.>`. The
library sets `NatsOpts.InboxPrefix` via `AppSlug.ClientInboxPrefix` from the
token's `sub` (falling back to `azp`) automatically — if you build your own
`NatsOpts`, use `CalloutNatsOpts.CreateAsync` so request/reply keeps working.

## Identity propagation

Client side — attach your signed assertion to outgoing messages:

```csharp
var nats = connection.Connection!;   // non-null once ConnectAsync has completed
var kv = nats.CreateKeyValueStoreContext();
var descriptor = await CalloutDiscovery.FetchAsync(kv, "orders");
using var header = new CalloutIdentityHeader(kv, descriptor, presentedToken);
var headers = await header.AttachAsync(null);   // null when propagation is disabled
await nats.PublishAsync("orders.commands", payload, headers: headers);
```

Service side — verify before trusting:

```csharp
var resolver = new CalloutIdentityResolver(kv, descriptor);
var claims = await resolver.VerifyAsync(headerValue);   // null = unverified, never throws
if (claims is not null)
{
    var oidcToken = await resolver.ResolveOidcTokenAsync(claims.Id); // needs autoAddSessionAccess
}
```

With DI (`AddCalloutNatsClient`), resolve `CalloutIdentityServices` and call
`GetIdentityVerifierAsync()` once the connection is up.

See the [identity propagation guide](./identity-propagation.md) for the full
sequence and trust model.

## Samples

[`dev/samples`](https://github.com/ryandanthony/nats-auth-callout-service/tree/main/dev/samples)
in the repo run entirely on this package (and the Wolverine one): `NoteService`
and `TaskService` are end-to-end Wolverine apps, `LoadService` drives raw
connections through `CalloutNatsOpts` under churn.
