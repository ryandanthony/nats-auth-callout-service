# 0016 — Token validation parameters

- **Status:** proposed
- **Date:** 2026-09-06
- **Deciders:** Ryan

## Context

The callout validates every presented OIDC/JWT bearer token before it can land on a profile ([ADR 0003](./0003-resolution-order-issuer-validate-app-profile.md), token-anchored trust). The .NET validation stack (`Microsoft.IdentityModel` / `System.IdentityModel.Tokens.Jwt`) exposes a `TokenValidationParameters` with defaults tuned for lenient interop, not for a system that mints short-lived NATS credentials off the result. Two defaults are actively dangerous here:

- **`ClockSkew` defaults to 5 minutes.** A token that expired up to five minutes ago would still validate, and the callout would mint a fresh NATS user JWT from it.
- **Algorithm handling is permissive.** If HMAC (`HS*`) algorithms are accepted while keys come from JWKS, an attacker can perform the classic **algorithm-confusion** attack: sign a forged token with `HS256` using the issuer's *public* key as the shared secret, and a naive validator that fetched that public key from JWKS accepts it.

Because validation is the single gate for all trust, its parameters must be **explicit, strict, and fail-closed**, and they must live with the issuer, never with a permission profile (a profile must never be able to weaken validation).

## Decision

Validation parameters are fixed by the design and configured only at the **issuer** level ([04 — Configuration](../design/04-configuration.md)). Profiles cannot set them.

### Always on, non-configurable

- **`ValidateIssuer = true`**, `ValidIssuer` pinned to the issuer discovered from `authority` (`{authority}/.well-known/openid-configuration` → `issuer`). This is what stops a valid token for one application's issuer being accepted for another.
- **`ValidateLifetime = true`** — `exp` and `nbf` enforced.
- **`RequireSignedTokens = true`**, **`RequireExpirationTime = true`**.
- **Signing keys come only from the cached JWKS**, resolved by the token header `kid` ([JWKS cache manager](../design/08-technical-architecture-csharp.md#jwks-cache-manager-keeping-the-idp-off-the-hot-path)). Embedded `jwk`/`x5c`/`jku` in the token header are ignored. An unknown `kid` fails closed (and triggers a single-flight JWKS refresh).
- **`alg: none` is always rejected** (implied by `RequireSignedTokens`, stated explicitly).

### Strict defaults, per-issuer overridable

| Parameter | Default | Rule |
|-----------|---------|------|
| `clockSkew` | **`60s`** | Duration string. Covers real NTP drift only. Config-load rejects a value above a hard cap (**`300s`**). Never the framework's 5-minute default. |
| `allowedAlgorithms` | asymmetric set: `RS256/384/512`, `ES256/384/512`, `PS256/384/512` | Explicit allow-list mapped to `TokenValidationParameters.ValidAlgorithms`. **`HS*` and `none` are never allowed**, listing either is a config-load error. |
| `validateAudience` | **true** | When true, `audience` is **required** (config-load error if absent). `false` is a deliberate, documented opt-out for providers that don't stamp `aud` on access tokens. |
| `audience` | (none) | One or more accepted audiences → `ValidAudiences`. Required when `validateAudience` is true. |

### Why HS\* is excluded, not merely defaulted off

Accepting an HMAC algorithm while trusting JWKS-published public keys enables algorithm confusion: the "secret" for `HS256` verification would be the issuer's public key, which the attacker also has. The callout only ever validates against asymmetric JWKS keys, so HMAC has no legitimate use here. The allow-list therefore **cannot** contain `HS*`; this is enforced at config load, not left to an operator to remember.

### Placement

These fields extend the existing per-issuer `issuer` block in the canonical model ([ADR 0007](./0007-internal-configuration-model.md)); they are not a new config section and not per-profile. All applications sharing an issuer get consistent validation — **enforced at config load**: the aggregate validator's `divergent-issuer-config` rule rejects any config where two applications share an `authority` but differ in any issuer-block field (structural equality over the whole `IssuerConfig`). Validation runs once per token against the shared authority's parameters, so a divergent block would otherwise make effective strictness depend on candidate ordering.

### Config-load validation (fail closed, [ADR 0006](./0006-config-reload-and-failure-semantics.md))

The whole batch is rejected (keep last-good) if any issuer:

- lists `HS*` or `none` in `allowedAlgorithms`, or lists an unknown algorithm;
- sets `validateAudience: true` with no `audience`;
- sets `clockSkew` above the cap or negative.

### Transport for discovery/JWKS fetches

Discovery and JWKS documents are fetched over HTTPS only (`HttpDocumentRetriever.RequireHttps = true`). Amendment (2026-11, [issue #2](https://github.com/ryandanthony/nats-auth-callout-service/issues/2)): the bootstrap flag `jwks.allowInsecureHttp` (default `false`) relaxes **only** this transport check, for dev/test setups where a containerized callout talks to a plain-HTTP dev IdP and a trusted certificate isn't practical inside the container. It is global (not per issuer), it is bootstrap-only so routing config can't turn it on, and the service writes a warning to stderr at startup when it is set. Every validation parameter in this ADR still applies unchanged. Production deployments must leave it off.

## Consequences

- Expired-token acceptance shrinks from 5 minutes to 60 seconds by default, bounded and explicit.
- Algorithm confusion is structurally impossible: HMAC can't be configured, keys come only from JWKS by `kid`, embedded header keys are ignored.
- Audience validation is on by default and can't be "half configured" (true without an audience is rejected rather than silently skipped).
- Validation strength is a property of the issuer, so no profile or routing change can weaken it.
- Slightly more config surface per issuer, but with safe defaults so a minimal issuer block (`authority` + `audience`) is fully strict already.
- Providers that legitimately omit `aud` require an explicit `validateAudience: false`, making that trade-off visible in config and review.

## Related

- Design: [04 — Configuration](../design/04-configuration.md) (issuer model), [08 — Technical architecture (C#)](../design/08-technical-architecture-csharp.md#token-validation) (implementation), [09 — Schemas and versioning](../design/09-schemas-and-versioning.md) (issuer schema).
- ADRs: [0002](./0002-service-auth-via-oidc-not-known-keys.md) (OIDC trust), [0003](./0003-resolution-order-issuer-validate-app-profile.md) (validate before app/profile), [0006](./0006-config-reload-and-failure-semantics.md) (reject-on-invalid), [0007](./0007-internal-configuration-model.md) (issuer in canonical model).
