# P19 — Client library (`AuthCallout.Client`)

**Wave:** post-v0.1 feature work
**Depends on:** shipped packages (`AuthCallout.Contracts`, `AuthCallout.Identity`), P14 (integration harness), P15 (samples, rewritten here)
**Blocks:** P20 (Wolverine integration)
**Estimated size:** 3–4 agent-days
**Grounding:** ADR 0020 (decision), ADR 0021 (retry semantics), ADR 0002 (services are OIDC principals), doc 06 (identity propagation flows), ADR 0012/0013 (discovery, naming)

## Scope

A new packable project, `src/AuthCallout.Client` (NuGet `AuthCallout.Client`,
published automatically by `release.yml`'s Trusted Publishing job via the
`AuthCallout.*` glob — confirm the nuget.org policy covers the new id). It gives
a raw NATS.Net consumer, user client or service alike:

1. Token acquisition + refresh (client credentials built in, user flows pluggable).
2. Correct `NatsOpts` construction (bearer token, scoped inbox prefix).
3. A managed connection with ADR 0021's retry/auth-violation semantics.
4. Discovery, self-assertion fetch, `Callout-Identity` attach (client side).
5. Assertion verification + OIDC token resolution (service side).
6. Optional `Microsoft.Extensions.DependencyInjection` wiring.

Dependencies: `AuthCallout.Contracts`, `AuthCallout.Identity`, `NATS.Net`
(pinned 3.2.0), `Microsoft.Extensions.{DependencyInjection.Abstractions,Options,Logging.Abstractions}`.
**No Wolverine** (ADR 0022). Target `net10.0` like the rest of `src/`.

## Sources to promote (steal, don't rewrite)

| New piece | Source | Changes |
|---|---|---|
| Retry/auth-violation policy | A production consumer's hardened NATS client | Semantics codified in ADR 0021; re-implement there without its Polly/UI coupling |
| Anonymous vs authenticated split | Same consumer's client + inbox-prefix helper | Anonymous inbox prefix becomes an option |
| Inbox prefix derivation | samples `CalloutInboxPrefix` | One implementation on `OidcTokenClaims` + `AppSlug.ClientInboxPrefix`, plus the anonymous/no-token path |
| Token refresh near `exp` | samples `LoadService/RefreshingCalloutConnection` | Generalize into `ClientCredentialsTokenProvider`; `TimeProvider`-injected |
| Client-credentials fetch | samples `DevOidcTokenFetcher` | ROPC → client credentials; authority/OIDC-discovery support; `IHttpClientFactory`-friendly |
| Discovery / self-assertion / resolver / verifier / context | samples `Shared/*` | Promote near-verbatim; cancellation tokens, logging, API polish |
| `OidcTokenClaims` | samples | Promote as public |

## Files owned

```
src/AuthCallout.Client/**
tests/AuthCallout.Client.Tests/**
tests/AuthCallout.Integration/Scenarios/ClientLibrary*.cs
dev/samples/**                       (rewritten on the package in task 7)
docs-site/** (client-library guide page)
```

## Tasks

### 1. Token providers (`AuthCallout.Client.Tokens`)

```csharp
public interface ICalloutTokenProvider
{
    ValueTask<string?> GetTokenAsync(CancellationToken ct);   // not-about-to-expire token
    ValueTask InvalidateAsync(CancellationToken ct);          // force refresh (ADR 0021 rule 2)
}
```

- `ClientCredentialsTokenProvider` (service default, ADR 0002): token endpoint or
  authority + OIDC discovery; client id; secret via inline/env/file (reuse the
  `SecretRef` shapes from `AuthCallout.Contracts.Primitives`); optional
  scopes/audience. Refresh per ADR 0021 rule 5 (30s margin, 5s backoff on
  failed re-mint, stale token retained).
- `StaticTokenProvider` (tests, CLI tools), `DelegateTokenProvider`
  (`Func<CancellationToken, ValueTask<string?>>` — an app with its own auth
  stack plugs in here).
- Promote `OidcTokenClaims` (unvalidated `sub`/`azp`/`exp`/`jti` reads).

### 2. Connection options builder

Promote `CalloutConnection.PrepareAsync` minus the dev-Keycloak coupling:

```csharp
var opts = await CalloutNatsOpts.CreateAsync(new CalloutClientOptions
{
    Url = "nats://...",
    ApplicationName = "notes",       // AppSlug.From() internally
    TokenProvider = provider,        // or Anonymous = true
    Name = "notes-worker",
}, ct);
```

- `AuthOpts.Token` + `InboxPrefix = AppSlug.ClientInboxPrefix(principal)`
  (`sub` → `azp` fallback; `AppSlug` is the single naming source, ADR 0013).
- Anonymous mode: caller-configurable anonymous inbox prefix, documented default
  `{slug}._INBOX` (app policy, not NACS policy).
- Raw `NatsOpts` is exposed for callers who own their connection (and for P20).

### 3. Managed connection (`CalloutNatsConnection`)

`ConnectAsync` / `ConnectAnonymousAsync` / `DisposeAsync`, `State` +
`StateChanged` (Connecting/Connected/Retrying/Failed). Implements ADR 0021
exactly: unbounded backoff on transport failures; one forced-refresh retry on
`Authorization Violation` (inner-exception walk); terminal
`CalloutAuthenticationRequiredException` on null token; `MaxReconnectRetry = 0`
with a self-owned reconnect loop that re-reads the token and recomputes
`InboxPrefix` per (re)connect. Polly-free (small internal backoff loop).

### 4. Identity propagation (`AuthCallout.Client.Identity`)

- First, in `AuthCallout.Identity`: add a **verify-only entry point** so
  verification no longer requires a throwaway active signing key (ADR 0020
  consequence).
- Promote: `DiscoveryDescriptor` + `CalloutDiscovery.FetchAsync` (keep the
  provisioning-race bounded retry), `SelfAssertion.FetchAsync` (`jti` → `sub`
  via `AppSlug.DeriveKeySegment`), header attach helper (no-op when descriptor
  says `enabled: false`, assertion cached per connection),
  `CalloutIdentityResolver` (per-`kid` key cache, `TimeProvider` expiry check,
  null = unverified, never throws on forged input, `ResolveOidcTokenAsync` for
  `autoAddSessionAccess` profiles), and the framework-neutral
  `ICalloutIdentityVerifier` + `CalloutIdentityContext` seam P20 binds to.

### 5. DI extensions

`services.AddCalloutNatsClient(o => { ...; o.UseClientCredentials(cc => ...); })`
registers `ICalloutTokenProvider`, `CalloutNatsConnection` (singleton),
`INatsConnection`, `ICalloutIdentityVerifier`, and a discovery cache.

### 6. Tests

- Unit (`tests/AuthCallout.Client.Tests`): refresh-deadline math and failed
  re-mint backoff with `FakeTimeProvider`; secret sourcing; inbox-prefix
  derivation incl. anonymous; auth-violation detection.
- Integration (existing P14 harness: real NATS + Keycloak + NACS): connect as
  service via client credentials; the ADR 0021 auth scenarios (one retry on
  violation, fast terminal failure on bad token, reconnect succeeds with
  refreshed token, terminal on null token); scoped-inbox isolation; full
  propagation round trip (attach → verify → resolve OIDC token); forged/expired
  header rejected; `enabled: false` short-circuits both sides.

### 7. Samples + docs

- Rewrite `dev/samples/Shared` as thin wrappers over the package (samples become
  living documentation and give the integration suite end-to-end package
  coverage); `NoteService`/`TaskService`/`LoadService` consume it.
- Docs-site: "Client library" guide (user client, service via client
  credentials, identity both directions); link from identity-propagation and
  getting-started; add the package to the README table.

## Definition of done

- `AuthCallout.Client` on nuget.org via a tagged release; samples and
  integration suite run on the package, green in CI.
- The acceptance bar: an existing consumer with hand-rolled connect and
  inbox-prefix plumbing could adopt the library as a drop-in replacement.

## Open questions

1. Single package vs. splitting out `AuthCallout.Client.Identity` — leaning
   single (the identity half adds no extra dependencies).
2. Multi-target `net9.0`? Only if a consumer needs it; start `net10.0`.
3. Anonymous inbox default: ship `{slug}._INBOX` as default, or require explicit
   configuration when `Anonymous = true`?
