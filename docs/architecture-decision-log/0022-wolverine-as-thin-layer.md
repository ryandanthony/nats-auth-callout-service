# 0022 — Wolverine support as a thin layer over the client library

- **Status:** proposed
- **Date:** 2026-11-15
- **Deciders:** Ryan

## Context

Wolverine (`WolverineFx.Nats`) is the messaging framework used by the known
consumers: mise's API and NACS's own dev samples both run Wolverine over NATS.
Running Wolverine behind the callout needs the same pieces as a raw client —
bearer-token connect, scoped inbox prefix, token refresh — plus two
Wolverine-shaped concerns:

- **Outbound:** attach the `Callout-Identity` assertion header to published
  messages (client side of doc 06).
- **Inbound:** verify that header before a handler runs and expose the verified
  caller to the handler (service side of doc 06; today sketched as
  `CalloutIdentityMiddleware` in `dev/samples/Shared`, and solved differently in
  mise with a raw-JWT `Authorization` header + `MiseUserContextMiddleware`).

Mise's API currently sidesteps all of this with an NKey `auth_users` bypass. The
target state (mise spec 022 Phase 6, ADR 0002) is services authenticating with
client-credentials JWTs like everyone else, which makes Wolverine-behind-the-
callout the normal case, not a demo.

## Decision

Ship **`AuthCallout.Client.Wolverine`** as a separate package that is a thin
adapter over `AuthCallout.Client` (ADR 0020):

- All token, connection, inbox, discovery, and verification logic lives in
  `AuthCallout.Client`. The Wolverine package contains only glue:
  transport configuration (feed the client library's `NatsOpts`/token into
  `UseNats`), an envelope-mapping hook that attaches the identity header on
  send, and `Before`-convention middleware that verifies it on receive and
  injects a verified identity context into handlers.
- The verification seam (`ICalloutIdentityVerifier` + identity context) is
  defined framework-neutral in `AuthCallout.Client`; the Wolverine middleware
  is one binding of it. A MassTransit or raw-subscriber binding would be
  another, with no changes to the core.
- `AuthCallout.Client` takes **no** Wolverine dependency, and the Wolverine
  package takes no dependency on NACS server internals. Wolverine versions can
  rev (it moves fast) with only the adapter package needing updates.
- The signed assertion (ADR 0011) is the identity mechanism, not a raw
  client-forwarded JWT header: Wolverine handlers trust the callout's
  signature, not a client-settable header. (Mise's `Authorization`-header
  middleware is the prior art being replaced, since it re-validates raw JWTs
  per message and trusts the client to attach them.)

## Consequences

- Wolverine consumers get connect + identity in a few lines; raw NATS.Net
  consumers are never forced to pull Wolverine.
- The adapter package is small enough that supporting it across Wolverine major
  versions is cheap; the behavioral contract lives (and is tested) in
  `AuthCallout.Client`.
- Wolverine transport internals (envelope mapping, listener setup) constrain
  what the adapter can intercept; any gap discovered (e.g. no clean per-send
  header hook) is resolved in the adapter, never by leaking Wolverine types
  into `AuthCallout.Client`.
- Mise's eventual Phase 6 migration (API off the NKey bypass) has a supported
  path.

## Related

- ADRs: [0002](./0002-service-auth-via-oidc-not-known-keys.md),
  [0011](./0011-signed-identity-assertion.md),
  [0020](./0020-ship-client-integration-libraries.md),
  [0021](./0021-client-connection-retry-semantics.md).
- Implementation: [P20 — Wolverine integration](../../implementation-plan/20-wolverine-integration.md).
- Prior art: `dev/samples/Shared/CalloutIdentityMiddleware.cs`, mise
  `MiseUserContextMiddleware`.
