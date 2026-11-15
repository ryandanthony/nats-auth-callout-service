---
sidebar_position: 8
---

# Wolverine guide

`AuthCallout.Client.Wolverine` runs a [Wolverine](https://wolverinefx.net/) host
— client or service — through the callout: bearer-token transport auth, outbound
`Callout-Identity` attach, and inbound verification middleware, as a thin layer
over [`AuthCallout.Client`](./client-library.md). Grounds:
[ADR 0022](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0022-wolverine-as-thin-layer.md).

```bash
dotnet add package AuthCallout.Client.Wolverine
```

## A service host in a few lines

A Wolverine service needs **zero NATS server config** — no NKey, no `auth_users`
bypass. It authenticates with a client-credentials JWT like every other
principal ([ADR 0002](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0002-service-auth-via-oidc-not-known-keys.md))
and gets its permissions from its profile.

```csharp
using AuthCallout.Client.Wolverine;

builder.UseWolverine(opts =>
{
    opts.UseNatsWithCallout(callout =>
    {
        callout.Url = "nats://nats:4222";
        callout.ApplicationName = "orders";
        callout.UseClientCredentials(cc =>
        {
            cc.Authority = new Uri("https://id.example.com/realms/prod");
            cc.ClientId = "orders-service";
            cc.ClientSecret = SecretRef.FromFile("/secrets/orders-client-secret");
        });
        // or: callout.TokenProvider = new DelegateTokenProvider(...) for a custom auth stack
    });

    opts.ListenToNatsSubject("orders.commands");
    opts.PublishMessage<OrderShipped>().ToNatsSubject("orders.events");
});
```

`UseNatsWithCallout` returns the normal `NatsTransportExpression`, so JetStream
and other transport configuration chain as usual.

## What it wires up

- **Transport auth.** The bearer token flows through the transport's
  `AuthCallback`, which NATS.NET invokes on **every (re)connect** — the token is
  never stale, satisfying [ADR 0021](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0021-client-connection-retry-semantics.md)'s
  reconnect rules. The token provider handles proactive refresh.
- **A scoped KV connection.** Wolverine's own message bus never uses NATS.NET
  request/reply inboxes (its reply subject is `wolverine.response.{node}`), so
  the scoped-inbox requirement only applies to the raw JetStream KV reads the
  identity features need. The package opens one correctly-scoped
  `CalloutNatsConnection` for those, separate from Wolverine's transport
  connection.
- **Outbound identity.** At host startup the package fetches the app's discovery
  descriptor and this connection's signed assertion, then stamps the
  `Callout-Identity` header onto every outgoing envelope. When the descriptor
  says identity propagation is disabled, nothing is attached.
- **Inbound verification.** `CalloutIdentityMiddleware` runs before every
  handler by default: it verifies the envelope's `Callout-Identity` assertion
  (signature against the keys bucket, expiry) and either passes a populated
  `CalloutIdentityContext` into the handler or stops the chain. A handler never
  runs on a missing, forged, or expired identity.

## Using the verified identity in a handler

The middleware injects the context as a handler parameter:

```csharp
public static class ShipOrderHandler
{
    public static void Handle(ShipOrder command, CalloutIdentityContext identity, OrderStore store)
    {
        // identity.Subject is the verified caller, not a client-settable header
        store.Ship(command.OrderId, requestedBy: identity.Subject);
    }
}
```

## Exempting anonymous endpoints

Verification is policy-with-opt-out: every handler chain is verified unless its
message type opts out, for subjects that legitimately take anonymous traffic
(registration, discovery):

```csharp
[RequireIdentity(false)]
public sealed record GetAppConfig();
```

## Migrating off an NKey bypass

An existing Wolverine app typically connects with a privileged NKey listed in
`auth_users` (full `>` permissions, skipping the callout). To migrate:

1. Create an OIDC client (client credentials) for the service in your realm.
2. Add a profile for it in the application's config, matched on `azp`, granting
   exactly the subjects it listens and publishes on (plus
   `autoAddSessionAccess` if it resolves caller OIDC tokens).
3. Replace `UseNats(...)` + the NKey with `UseNatsWithCallout(...)` as above.
4. Remove the NKey from the NATS server's `auth_users` and account config.

The repo's [`dev/samples`](https://github.com/ryandanthony/nats-auth-callout-service/tree/main/dev/samples)
(`NoteService`, `TaskService`) are complete Wolverine hosts on this package,
exercised by the Aspire dev loop (`dotnet run --project dev/AppHost.Local`).
