# 0011 — Callout-signed identity assertion (sole identity-propagation mechanism)

- **Status:** proposed
- **Date:** 2026-09-05
- **Deciders:** Ryan

## Context

A subscriber cannot see who published a message: NATS exposes only the subject, headers, and payload, never the sender's connection or token. The identity of the publishing principal must therefore travel in-band, and be **trustworthy** on arrival.

The only per-publisher attribute NATS enforces is the subject, but embedding identity in subjects constrains subject design and couples authorization subjects to identity. Any other carrier (header, payload) is client-settable, so to be trusted it must be **cryptographically verifiable** by the subscriber. This ADR makes a callout-signed assertion the single identity carrier.

## Decision

The callout mints a **signed identity assertion** at authorization time. The client fetches its own assertion from KV and attaches it as a message header; any subscriber verifies it by signature. This is the **sole identity-propagation mechanism** — identity is not embedded in subjects.

### The assertion

- A compact signed token (a small JWT) whose claims bind the connection to its identity: at least the callout-generated **`id`** (ADR 0010, the key into the token store), `sub`, `app` (application slug), `iss` (original OIDC issuer), and `exp` (aligned to the token/grant lifetime). It carries no raw OIDC token and no secrets.
- **Signed with a dedicated callout assertion key**, not the NATS issuer signing seed (ADR 0007), so identity verification is decoupled from the NATS account signing key and rotates independently. Only the assertion **public** key is ever published (below).
- Distinct from the minted NATS user JWT (which authorizes NATS actions) and from the stored OIDC token (ADR 0010); the assertion exists solely to convey verifiable identity in-band and to name the id that resolves the stored token.

### Public key in a well-known KV location

The callout publishes its assertion **verification (public) key** to a fixed, well-known KV location so verifiers stay entirely within NATS:

```
callout.assertion-keys.current    (pointer to the current key)
callout.assertion-keys.{kid}      (per-key-id entries for rotation)
```

- A dedicated **keys bucket** holding only public keys (never seeds). It is **readable only by authenticated connections**: the callout adds read access to the keys bucket to every profile grant (any successfully authorized connection), but **not** to the anonymous union grant (ADR 0004), so anonymous clients cannot read it. This is separate from the per-application token buckets (ADR 0010), which stay restricted to their own application.
- Each assertion carries a `kid` header; verifiers fetch `callout.assertion-keys.{kid}` and cache it. On rotation the callout writes the new key under its new `kid` and updates the `callout.assertion-keys.current` pointer, keeping recent old keys readable until their assertions expire.
- The callout writes/refreshes this entry at startup and on key rotation. It is the only writer; the bucket is read-only to authenticated connections.

### Delivery

Because the callout replies to the NATS **server**, not the client, it cannot hand the assertion straight to the client. Instead (see ADR 0010 for the buckets):

1. At authorization the callout generates the connection's `id`, stores the OIDC token at `callout.apps.{app-slug}.oidc-tokens.{id}`, signs the assertion binding that `id`, and stores the **assertion** at `callout.apps.{app-slug}.assertions.{jti}`.
2. After connecting, the client `GET`s **its own** entry `callout.apps.{app-slug}.assertions.{jti}` (keyed by the `jti` it already knows) once, retrieving its signed assertion, and caches it for the connection's life.
3. The client attaches the assertion as the `Callout-Identity` header (see the header naming convention in ADR 0010) on the messages it publishes.
4. A subscriber verifies the header's signature against the callout public key (fetched from `callout.assertion-keys.{kid}` and cached), checks `exp` and `app`, and trusts the contained `id`/`sub`. It then `GET`s the full OIDC token from `callout.apps.{app-slug}.oidc-tokens.{id}` if it needs richer claims.

### Trust

- Trust comes from the **callout's signature**, so identity needs no subject involvement and no enforced carrier.
- A forged or tampered header fails signature verification and is rejected.
- The generated `id` is opaque and only resolvable by connections granted `AutoAddSessionAccess` for that application; `exp` bounds replay; `app` scopes the assertion to one application.

## Consequences

- Identity propagation is uniform and subject-independent: subjects carry only authorization structure (ADR 0009), never identity.
- Subscribers need only the callout's public key (read once from the keys bucket) to verify identity, and a token-bucket lookup only when they want claims beyond the assertion. No non-NATS endpoint is involved.
- Keys-bucket read access is automatically added to every profile grant, so any authenticated connection can verify assertions while anonymous connections cannot. This mirrors the automatic grant mechanism used for session access (ADR 0010).
- New responsibilities: the callout mints and signs an assertion per connection, stores it for self-lookup, and publishes its verification key; clients perform a one-time self-lookup and echo the header.
- The assertion is a signed-token format to version and keep secret-free (reuse the ADR 0008 `schemaVersion` discipline).
- Key management: rotating the assertion signing key is handled in-band by writing the new `kid` entry and updating the `callout.assertion-keys.current` pointer. The keys bucket must never hold private seeds.
- A message with no valid `Callout-Identity` header conveys no identity; subscribers that require identity reject it. There is no fallback identity carrier.

## Related

- Design: [02 — Authorization model](../design/02-authorization-model.md#identity-propagation-adr-0010-0011), [03 — Resolution pipeline](../design/03-resolution-pipeline.md), [05 — Runtime and lifecycle](../design/05-runtime-and-lifecycle.md).
- ADRs: [0010](./0010-session-jwt-store-in-kv.md) (generated id, token + id-lookup buckets that carry the assertion), [0007](./0007-internal-configuration-model.md) (assertion signing key), [0003](./0003-resolution-order-issuer-validate-app-profile.md) (assertion minted only after validation), [0012](./0012-identity-propagation-toggle-and-discovery.md) (per-application toggle and discovery descriptor for this mechanism).
