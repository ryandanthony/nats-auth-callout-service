# P20 — Wolverine integration (`AuthCallout.Client.Wolverine`)

**Wave:** post-v0.1 feature work
**Depends on:** P19 (client library; token providers, managed opts, `ICalloutIdentityVerifier` seam)
**Blocks:** nothing
**Estimated size:** 2–3 agent-days
**Grounding:** ADR 0022 (decision), ADR 0020/0021 (client library + retry contract), ADR 0002 (service auth), ADR 0011 (signed assertion), doc 06 (flows)

## Scope

A new packable project, `src/AuthCallout.Client.Wolverine` (NuGet, same
`release.yml` flow as P19). A thin adapter per ADR 0022: all behavior lives in
`AuthCallout.Client`; this package only binds it into Wolverine's NATS
transport and handler pipeline.

Dependencies: `AuthCallout.Client`, `WolverineFx`, `WolverineFx.Nats`. The
published package declares a **minimum** of 6.47.0 (the first release with the
`ConfigureNatsOpts` seam) through `VersionOverride` in the csproj. NuGet reads that
as `>= 6.47.0`, so consumers can float to any newer Wolverine. The repo's central
version (6.64.0 in `Directory.Packages.props`) only drives the tests and dev
samples. Target `net10.0`.

Serves both roles:

- **Client role** (an app publishing commands/queries through Wolverine):
  token-authenticated connect + identity header attached on send.
- **Service role** (a Wolverine host handling messages and talking to the DB):
  same connect via client-credentials JWT (ADR 0002 — no NKey in NATS config),
  plus inbound verification middleware.

## Prior art to steal

| Piece | Source | Notes |
|---|---|---|
| Transport setup behind the callout | `dev/samples/*/Program.cs` + `CalloutConnection` | Samples already run Wolverine over NACS: `UseNats` fed with token + scoped `InboxPrefix` |
| Inbound verification middleware | `dev/samples/Shared/CalloutIdentityMiddleware.cs` | `Before`-convention middleware returning `(HandlerContinuation, CalloutIdentityContext?)`; Stop on missing/forged header |
| Late transport config patching | Field experience with `WolverineFx.Nats` | `UseWolverine` reads config eagerly pre-Build; capture `NatsTransportConfiguration` and patch URL/auth in a config hook. Same trick applies for injecting the token `AuthCallback` (an `AuthCallback` is also the proven workaround for NKey seed handling in NATS.NET 2.7.x) |
| Scoped listener/middleware wiring | Field experience: per-subject listeners + `Before` auth middleware | The raw-JWT `Authorization`-header approach is what the signed assertion replaces (ADR 0022) |

## Files owned

```
src/AuthCallout.Client.Wolverine/**
tests/AuthCallout.Client.Wolverine.Tests/**
tests/AuthCallout.Integration/Scenarios/Wolverine*.cs
dev/samples/**                      (switched onto this package)
docs-site/** (wolverine guide page)
```

## Tasks

### 1. Transport extension

```csharp
host.UseWolverine(opts =>
{
    opts.UseNatsWithCallout(callout =>
    {
        callout.Url = "nats://...";
        callout.ApplicationName = "notes";
        callout.UseClientCredentials(cc => { ... });   // or callout.TokenProvider = ...
    });
});
```

- Builds the `NatsOpts` via P19's builder (token + scoped inbox prefix) and
  feeds it into `WolverineFx.Nats`'s transport configuration, using an
  `AuthCallback` so **every (re)connect** pulls a current token from the
  `ICalloutTokenProvider` (ADR 0021 rules 4–5; Wolverine owns the connection
  object, so the token must flow through the callback rather than P19's
  `CalloutNatsConnection`).
- Registers the P19 DI services (`ICalloutTokenProvider`,
  `ICalloutIdentityVerifier`, discovery cache) into the host container.
- **Resolved:** WolverineFx.Nats 6.33 had no `InboxPrefix` seam, so the first
  implementation opened a second, scoped connection for KV reads. 6.47 added
  `NatsTransportConfiguration.ConfigureNatsOpts`; the adapter now bumps to it,
  sets the scoped `InboxPrefix` on Wolverine's own transport connection
  (prefix computed by a hosted service ordered before Wolverine's runtime, since
  the hook is synchronous), and runs all discovery/identity KV reads over that
  single connection. Caveat: the principal is computed once from the first
  token, which is correct for client-credentials services (stable `azp`).

### 2. Outbound: identity header on send

- An envelope-mapping / sending hook that attaches the connection's cached
  signed assertion as the `Callout-Identity` header (name from the discovery
  descriptor) on every outgoing message; no-op when the descriptor says
  `enabled: false`.
- The assertion fetch (discovery + self-assertion, P19) runs lazily on first
  send and is cached for the life of the connection; refreshed after reconnect.

### 3. Inbound: verification middleware

Promote `CalloutIdentityMiddleware` into the package:

- `Before`-convention middleware bound to handler chains via a marker
  (`IClientRequest`-style interface, attribute, or a policy that applies it to
  all NATS listeners — decide during implementation; default to the policy so
  apps can't forget a handler).
- Reads `Callout-Identity` off the envelope, verifies via P19's
  `ICalloutIdentityVerifier`, and either continues with a populated
  `CalloutIdentityContext` (injected into the handler as a parameter) or stops
  the chain. Optional configured behavior for stop: silent drop vs. error reply
  when `ReplyUri` is present (so fire-and-forget messages never get a reply).
- Optional `RequireIdentity = false` mode per listener for subjects that accept
  anonymous traffic (e.g. registration/discovery endpoints).

### 4. Tests

- Unit: middleware continuation logic (missing/forged/expired header → Stop;
  valid → Continue + context), header attach no-op when disabled, token
  callback invalidation path.
- Integration (P14 harness): a Wolverine client host and a Wolverine service
  host, both authenticating through NACS with client-credentials JWTs (no
  `auth_users` entries at all): request/reply round trip with verified
  identity; handler never runs on a forged header; service reconnects with a
  refreshed token after its first token expires (short `accessTokenLifespan`
  realm); scoped-inbox isolation between the two hosts.

### 5. Samples + docs

- Switch `NoteService`/`TaskService` onto the package (deleting what remains of
  `dev/samples/Shared` after P19).
- Docs-site: "Wolverine" guide — client host, service host (JWT instead of a
  named NATS user), identity middleware, migration notes for an existing
  Wolverine app moving off an NKey `auth_users` bypass.

## Definition of done

- `AuthCallout.Client.Wolverine` on nuget.org; both sample hosts run on it via
  `dotnet run --project dev/AppHost.Local`; integration scenarios green in CI.
- A Wolverine service needs zero NATS server config (no NKey, no `auth_users`)
  to connect with full scoped permissions — the ADR 0002 end state demonstrated.

## Open questions

1. Middleware binding default: marker interface vs. blanket policy on NATS
   listeners (leaning policy + opt-out).
2. Should the stopped-chain error reply be in scope, or leave reply semantics
   entirely to the app (some apps want a structured auth-error reply, others
   none at all)?
3. Wolverine major-version policy: pin to one major per package release, or
   widen the range once the adapter surface proves stable?
