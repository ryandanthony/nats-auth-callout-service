# 07 — KV storage

The authoritative reference for every JetStream KV bucket the callout owns. Grounds: [ADR 0010](../architecture-decision-log/0010-session-jwt-store-in-kv.md) (token + id-lookup buckets keyed by a generated id), [ADR 0011](../architecture-decision-log/0011-signed-identity-assertion.md) (assertion keys bucket), [ADR 0012](../architecture-decision-log/0012-identity-propagation-toggle-and-discovery.md) (per-application toggle and discovery descriptor), and [ADR 0013](../architecture-decision-log/0013-kv-bucket-naming-contract.md) (the physical bucket/key mapping). Where any other document disagrees about a key pattern, value, writer, reader, or TTL, **this table wins**. Dotted paths are the *logical* addresses; the **physical** column gives the real bucket + key per ADR 0013, and **grants always use the physical subjects** (`$KV.<bucket>.<key>`).

## Buckets

| Bucket | Logical path | Physical bucket + key (ADR 0013) | Value | Written when | Writer | Readers | TTL |
|--------|--------------|----------------------------------|-------|--------------|--------|---------|-----|
| Token store | `callout.apps.{app-slug}.oidc-tokens.{id}` | `callout_apps_{app-slug}_oidc_tokens` / `{id}` | The client's **OIDC access token** (raw JWT). Not the minted NATS user JWT. | On each successful, non-anonymous authorization for an app with identity propagation **enabled**, before the auth response is returned. | Callout only. | Profiles with `autoAddSessionAccess` for that application (read of `$KV.callout_apps_{app-slug}_oidc_tokens.>`). | Matched to the token/grant lifetime. |
| Id lookup | `callout.apps.{app-slug}.assertions.{jti}` | `callout_apps_{app-slug}_assertions` / `{jti}` | The client's **signed identity assertion** (ADR 0011), which contains the generated `id`. | Same authorization step, alongside the token write (enabled apps only). | Callout only. | The owning client reads **only its own** entry (`$KV.callout_apps_{app-slug}_assertions.{its-own-jti}`). | Same as the token record. |
| Assertion keys | `callout.assertion-keys.current` (current pointer) and `callout.assertion-keys.{kid}` (per-key-id) | `callout_assertion_keys` / `current`, `{kid}` | Assertion **public** verification key only. Never a private seed. | At startup and on key rotation. | Callout only. | Any authenticated (non-anonymous) connection: read of `$KV.callout_assertion_keys.>` is auto-added to every profile grant, regardless of the identity-propagation toggle. Anonymous grants get no access. | Long-lived; old `{kid}` entries kept readable until their assertions expire. |
| Discovery | `callout.apps.{app-slug}.discovery` | `callout_apps_{app-slug}_discovery` / `descriptor` | Per-application discovery descriptor (versioned JSON): app slug, `identityPropagation.enabled`, and when enabled the header name, bucket names, and current `kid`. Secret-free. | At startup and whenever config or the assertion key changes. | Callout only. | Any authenticated (non-anonymous) connection with a grant in that application (read of `$KV.callout_apps_{app-slug}_discovery.descriptor`), regardless of the toggle. Anonymous grants get no access. | Long-lived; refreshed on change. |

## Identity propagation toggle (ADR 0012)

Identity propagation is **per application** and defaults to enabled. When an application sets `identityPropagation.enabled = false`:

- No token record is written to `callout.apps.{app-slug}.oidc-tokens.{id}` and no assertion to `callout.apps.{app-slug}.assertions.{jti}`.
- The application's grants **still receive** keys-bucket and discovery reads (those are unconditional for every authenticated grant), but `autoAddSessionAccess` on any of its profiles is a config-validation error (nothing to read).
- The discovery descriptor is still published with `"enabled": false` (bucket/kid fields omitted), and the unconditional discovery read is what lets clients read it, skip the self-lookup, and lets subscribers know no `Callout-Identity` header will arrive.

Authorization, user-JWT minting, and outcome events are unaffected by the toggle.

## Key parts

- `{app-slug}` — a slugified, stable, collision-free form of the selected application's name, validated at config load. Per-app data lives under `callout.apps.{app-slug}.>`, so `callout.apps.>` scopes all applications while the global `callout.assertion-keys.>` stays separate. Applications stay isolated.
- `{id}` — the **callout-generated** high-entropy id for the connection, minted once per authorized connection. It is the correlation key into the token store and is not any token claim. A reconnect or token refresh is a new authorization and yields a new `id`; the old record lingers until its TTL.
- `{jti}` — the client's token `jti`, used **only** as the client's self-lookup key so it can find its own assertion. If a token has no `jti`, the callout falls back to `sub` (fixed, not configurable) or omits the id-lookup entry, in which case identity propagation is unavailable for that connection but the grant still stands. The physical key is the **derived** form of the raw value (`AppSlug.DeriveKeySegment`: sanitized text + a 16-hex SHA-256 suffix, ADR 0013), so sanitization aliases of different principals cannot collide; clients derive the same key from their own token when reading.
- `{kid}` — the assertion signing key id carried in each assertion header, so verifiers fetch the exact `callout.assertion-keys.{kid}` they need and cache it.

## Access model

- **Token read** requires a profile with `autoAddSessionAccess`; it grants read of the whole application token bucket (`callout.apps.{app-slug}.oidc-tokens.>`), read-only, scoped to that application. Intended for service profiles that resolve callers.
- **Id self-lookup** grants each client read of only its own `callout.apps.{app-slug}.assertions.{jti}` entry, so a client can discover its own `id` but no one else's.
- **Keys read** is auto-added to every profile grant, regardless of the application's identity-propagation toggle, so any authenticated connection can verify assertions; it is never added to the anonymous union grant.
- **Discovery read** is granted per application (`callout.apps.{app-slug}.discovery`) to that application's authenticated connections, regardless of the toggle (so a disabled app's clients can still discover `enabled: false`); never to the anonymous union grant.
- The callout is the **sole writer** to all buckets and requires JetStream KV write access to them.

## Invariants

- The stored token JWT is sensitive: the token bucket is readable only by the callout and explicitly granted service sessions, and TTL bounds exposure.
- Anonymous connections select no application and get **no token record and no assertion**.
- Both per-application buckets are namespaced by `{app-slug}`, so a token or assertion for one application is never resolvable from another.
- Trust in identity comes from the assertion's signature (verified with the keys bucket), never from a bucket key or a client-supplied header value.
- The keys bucket holds public keys only and must never contain a private seed.

See [06 — Identity propagation sequence](./06-identity-propagation-sequence.md) for how these buckets are read and written end to end, and [05 — Runtime and lifecycle](./05-runtime-and-lifecycle.md#identity-stores-adr-0010-0011) for the storage step in the request flow.
