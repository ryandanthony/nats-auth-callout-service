# AuthCallout.Client

Consumer-side integration library from [nats-auth-callout-service](https://github.com/ryandanthony/nats-auth-callout-service) (ADR 0020): everything a raw NATS.Net consumer — user client or service — needs to work correctly with the callout.

## What it gives you

1. Token acquisition and refresh: `ClientCredentialsTokenProvider` (ADR 0002, built in) or plug in your own via `DelegateTokenProvider`/`StaticTokenProvider`.
2. Correct `NatsOpts` construction (bearer token, scoped `_INBOX.{slug}.{principal}.>` prefix) via `CalloutNatsOpts.CreateAsync`.
3. A managed connection, `CalloutNatsConnection`, implementing ADR 0021's retry/auth-violation semantics: unbounded backoff on transport failures, exactly one forced-refresh retry on an Authorization Violation then fail terminally, a distinct `CalloutAuthenticationRequiredException` on a missing token (never silently anonymous), and self-owned reconnects (`MaxReconnectRetry = 0` plus a disconnect handler that re-enters the connect pipeline with a fresh token and recomputed inbox prefix).
4. Discovery, self-assertion fetch, and `Callout-Identity` header attach (client side, `AuthCallout.Client.Identity`).
5. Assertion verification and OIDC token resolution (service side, same namespace): `CalloutIdentityResolver` / `ICalloutIdentityVerifier`.
6. Optional `Microsoft.Extensions.DependencyInjection` wiring: `services.AddCalloutNatsClient(o => { ...; o.UseClientCredentials(cc => ...); })`.

## Quick start (service, client credentials)

```csharp
var opts = new CalloutClientOptions
{
    Url = "nats://localhost:4222",
    ApplicationName = "notes",
};
opts.UseClientCredentials(cc =>
{
    cc.Authority = new Uri("https://idp.example.com/realms/dev");
    cc.ClientId = "notes-worker";
    cc.ClientSecret = SecretRef.Inline("...");
});

await using var connection = new CalloutNatsConnection(opts);
await connection.ConnectAsync();
```

## Identity propagation

```csharp
var kv = connection.Connection!.CreateKeyValueStoreContext();
var descriptor = await CalloutDiscovery.FetchAsync(kv, "notes");

// Client side: attach the signed assertion to outgoing messages.
var header = new CalloutIdentityHeader(kv, descriptor, presentedOidcToken);
var headers = await header.AttachAsync(null);

// Subscriber side: verify an inbound message's header.
var resolver = new CalloutIdentityResolver(kv, descriptor);
var claims = await resolver.VerifyAsync(headers![CalloutIdentityHeader.FallbackName]!);
```

Docs: https://ryandanthony.github.io/nats-auth-callout-service/ (see Identity propagation).
