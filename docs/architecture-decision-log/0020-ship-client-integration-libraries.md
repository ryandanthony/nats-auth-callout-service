# 0020 — Ship client integration libraries

- **Status:** proposed
- **Date:** 2026-11-15
- **Deciders:** Ryan

## Context

NACS validates tokens and mints grants, but every consumer must hand-assemble the
client side: `NatsOpts` with a bearer token, the scoped inbox prefix
(`_INBOX.{slug}.{principal}.>`, the S2 fix), token refresh near `exp`, discovery
descriptor fetch, self-assertion fetch, `Callout-Identity` attach, and (service
side) assertion verification plus OIDC token resolution.

That code exists twice today, neither shipped:

- `dev/samples/Shared` (`CalloutConnection`, `CalloutInboxPrefix`,
  `CalloutDiscovery`, `CalloutSelfAssertion`, `CalloutIdentityResolver`,
  `OidcTokenClaims`, `RefreshingCalloutConnection`) — demo-grade, never packed.
- Mise (`NatsClientService`, `NatsInboxPrefix`) — production-hardened connect and
  retry logic built against NACS v0.1.3, duplicating the sample concepts.

The published packages (`AuthCallout.Contracts`, `AuthCallout.Core`,
`AuthCallout.Identity`) carry primitives only; the first real consumer (mise)
had to rebuild the integration layer, and the second would too.

## Decision

Ship a packable **`AuthCallout.Client`** library (NuGet, via the existing
`release.yml` Trusted Publishing job) that owns the consumer-side integration:
token acquisition and refresh (client credentials built in, user flows
pluggable), `NatsOpts` construction with the scoped inbox prefix, a managed
connection (retry semantics per ADR 0021), discovery, identity-header attach,
and service-side assertion verification.

**One package serves both users and services.** Per ADR 0002 a service is just
another OIDC principal (client-credentials JWT, profile matched on `azp`); the
only differences are how the token is obtained and which direction identity
flows (attach vs. verify), so splitting packages would force most consumers to
reference both.

The library is promoted from, not written beside, the existing code: the sample
helpers and mise's hardened logic are its sources, and `dev/samples/` is
rewritten on top of it so the integration suite exercises the shipped package.

Framework-specific layers (Wolverine, ADR 0022) build on this library and never
force a messaging framework on raw NATS.Net consumers: `AuthCallout.Client`
depends only on `AuthCallout.Contracts`, `AuthCallout.Identity`, and `NATS.Net`.

## Consequences

- Consumers get a supported, tested integration path; mise can delete
  `NatsInboxPrefix` and most of `NatsClientService`'s connect plumbing.
- The samples stop being the de facto client library and become living
  documentation of the real one.
- The client-facing behaviors (inbox naming, discovery shape, header name)
  gain a compatibility surface: breaking them now breaks a published package,
  not just docs.
- `AuthCallout.Identity` needs a verify-only entry point (today
  `AssertionSigner` demands a throwaway active signing key just to verify).

## Related

- ADRs: [0002](./0002-service-auth-via-oidc-not-known-keys.md) (services are OIDC
  principals), [0010](./0010-session-jwt-store-in-kv.md)/[0011](./0011-signed-identity-assertion.md)/[0012](./0012-identity-propagation-toggle-and-discovery.md)
  (the flows the library implements), [0013](./0013-kv-bucket-naming-contract.md)
  (`AppSlug` as the single naming source), [0021](./0021-client-connection-retry-semantics.md),
  [0022](./0022-wolverine-as-thin-layer.md).
- Implementation: [P19 — Client library](../../implementation-plan/19-client-library.md).
