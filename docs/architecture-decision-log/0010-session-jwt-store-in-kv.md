# 0010 — Callout-maintained token store keyed by a generated id

- **Status:** proposed
- **Date:** 2026-09-05
- **Deciders:** Ryan

## Context

The callout validates a client's token and knows its claims, but it is not in the message path. NATS delivers no verified publisher identity to subscribers: a received message exposes only its subject, headers, and payload, never the sender's connection or token. Services that need the connecting principal's identity (claims such as `email`, `roles`, `sub`, or the full token) therefore have no built-in way to obtain it.

We want services to retrieve the authenticated identity using a commonly known, standard artifact (the OIDC **JWT**), keyed by an identifier the callout controls rather than a provider-specific token claim.

## Decision

At authorization the callout **generates an id** for the connection and writes the client's validated **OIDC access token (JWT)** to a JetStream KV keyed by that generated id. A second KV lets the client discover its own id. Anonymous connections have no selected application and get **no token record**.

### Why a generated id (not `jti`)

The correlation key is a **callout-generated id** (a high-entropy GUID), minted once per authorized connection. It is not any provider claim:

- **Provider-neutral.** It depends on nothing in the token, so no assumption that a provider emits `jti`, `sid`, or any particular claim.
- **Callout-controlled.** The callout owns the id's format, uniqueness, and lifetime.
- **Per-connection.** A reconnect (or token refresh) is a new authorization and yields a **new** id and record; the old one lingers until its TTL. There is intentionally no SSO-session grouping.

Provider claims (`sub`, `jti`, roles, ...) are still **stored inside** the record and the assertion as identity attributes; they are just not the key.

### Storage: two buckets per application

One pair of KV buckets per application (namespaced by application slug):

```
callout.apps.{app-slug}.oidc-tokens.{id}    # OIDC access token, keyed by generated id
callout.apps.{app-slug}.assertions.{jti}    # the client's signed assertion, keyed by its token jti
```

**Token bucket** `callout.apps.{app-slug}.oidc-tokens.{id}`:

- `{app-slug}` — a slugified, stable, collision-free form of the selected application's name (validated at config load).
- `{id}` — the callout-generated id for the connection.
- Value — the client's **OIDC access token** (a standard, self-describing JWT any service can parse and, if it chooses, validate against the issuer's JWKS). The minted NATS user JWT is not stored.
- Written on a successful (non-anonymous) authorization, TTL aligned to the token/grant lifetime.

**Id-lookup bucket** `callout.apps.{app-slug}.assertions.{jti}`:

- Keyed by the client's token `jti` (the one value the client already knows about itself), used **only** so the client can discover its own generated id after connecting.
- Value — the client's **signed identity assertion** (ADR 0011), which contains the generated id. (Storing the assertion here, rather than a bare id, means the value the client fetches is already the trusted artifact it will attach to messages.)
- Same TTL as the token record.
- `jti` is used **only** as this self-lookup key. If a token carries no `jti`, the callout falls back to `sub` (fixed, not configurable) or omits the id-lookup entry, in which case identity propagation is unavailable for that connection but the grant still stands.

### Access grants

- **Id self-lookup:** the client is granted read of **only its own** entry `callout.apps.{app-slug}.assertions.{its-own-jti}`, so a client can discover its own id but not anyone else's.
- **Token read (`AutoAddSessionAccess`):** profiles gain an **`AutoAddSessionAccess`** option (default off). When enabled, the minted grant includes read access to the whole application token bucket (`callout.apps.{app-slug}.oidc-tokens.>`), read-only, scoped to that application. Intended for service profiles that resolve callers.

### Resolution flow

Callout, at authorization:

1. Mint the connection and generate the id.
2. Store the OIDC JWT at `callout.apps.{app-slug}.oidc-tokens.{id}`.
3. Sign an identity assertion binding the id (ADR 0011) and store it at `callout.apps.{app-slug}.assertions.{jti}`.
4. Grant the client read of `callout.apps.{app-slug}.assertions.{jti}` and read of the keys bucket (ADR 0011); grant service profiles `AutoAddSessionAccess` where configured.

Client:

1. Sign in with its OIDC JWT; 2. receive its grant; 3. `GET callout.apps.{app-slug}.assertions.{jti}` once to fetch its signed assertion, and cache it; 4. attach it as the `Callout-Identity` header (ADR 0011) on published messages.

Subscriber (identity resolution is via the assertion only, ADR 0011):

1. Fetch the assertion verification key from the keys bucket (cache it); 2. read the `Callout-Identity` header; 3. verify signature, `exp`, `app`; 4. extract the id and `GET callout.apps.{app-slug}.oidc-tokens.{id}`; 5. use the OIDC JWT to make decisions.

### Identity propagation is assertion-only

The **signed assertion (ADR 0011) is the sole identity-propagation mechanism.** Identity is not embedded in subjects: subjects carry only what the permission model needs (ADR 0009 templated subjects for authorization), not identity for downstream lookup.

### Message header naming convention

Identity-related message headers use the **`Callout-`** prefix, matching the `callout.*` KV namespace. This deliberately avoids the reserved **`Nats-`** prefix, which NATS and JetStream use for their own headers (`Nats-Msg-Id`, `Nats-Expected-Stream`, `Nats-Rollup`, ...); custom `Nats-*` headers risk colliding with reserved semantics and must not be used.

| Header | Set by | Trust | Purpose |
|--------|--------|-------|---------|
| `Callout-Identity` | caller | verifiable by signature | the callout-signed identity assertion (ADR 0011) |

The callout sets no message headers. There is deliberately no bare-id header: a raw id would be an untrusted, replayable bearer value. The only identity header is the signature-verified `Callout-Identity` assertion.

## Consequences

- The correlation key is provider-neutral: it depends on no token claim, so any OIDC provider works regardless of whether it emits `jti`, `sid`, etc.
- `jti` (or the `sub` fallback) is used only as the client's self-lookup key in `callout.apps.{app-slug}.assertions`, never as the identity key downstream.
- Downstream services obtain the authenticated identity through a standard OIDC JWT at a deterministic KV location, resolved from the verified assertion.
- Applications are isolated: both buckets are namespaced per application slug; `AutoAddSessionAccess` and id self-lookup grants are scoped per application.
- The callout requires JetStream KV write access to both buckets; its NATS user must be permitted to publish there.
- The stored JWT is sensitive: the token bucket is readable only by the callout and explicitly granted service sessions; TTL bounds exposure. The id-lookup bucket is readable by each client for its own entry only.
- Requires an application-name → slug rule that is stable and collision-free (validated at config load).
- Two buckets per application instead of one; the id-lookup bucket costs one extra write and one client read per connection.

## Related

- Design: [02 — Authorization model](../design/02-authorization-model.md), [03 — Resolution pipeline](../design/03-resolution-pipeline.md), [05 — Runtime and lifecycle](../design/05-runtime-and-lifecycle.md).
- ADRs: [0002](./0002-service-auth-via-oidc-not-known-keys.md) (service sessions), [0003](./0003-resolution-order-issuer-validate-app-profile.md) (JWT validated before storage), [0007](./0007-internal-configuration-model.md) (`AutoAddSessionAccess` profile field), [0008](./0008-publish-auth-outcome-events.md) (complementary event stream), [0011](./0011-signed-identity-assertion.md) (the signed assertion, sole identity-propagation mechanism), [0012](./0012-identity-propagation-toggle-and-discovery.md) (per-application toggle that disables this store, and discovery descriptor).
