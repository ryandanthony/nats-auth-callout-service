# 0012 — Per-application identity-propagation toggle and discovery descriptor

- **Status:** proposed
- **Date:** 2026-09-06
- **Deciders:** Ryan

## Context

Identity propagation (ADR 0010, 0011) makes the callout do extra work on every successful, non-anonymous authorization: generate an id, write the OIDC access token to `callout.apps.{app-slug}.oidc-tokens.{id}`, sign an identity assertion, and write it to `callout.apps.{app-slug}.assertions.{jti}`. It also publishes an assertion verification key to `callout.assertion-keys.current`. (Bucket paths in this ADR are logical addresses; [ADR 0013](./0013-kv-bucket-naming-contract.md) defines the physical bucket/key mapping.)

Not every application wants this. Some have no service that resolves caller identity, do not want raw OIDC tokens sitting in KV (even short-lived), or simply want to avoid the per-connection write cost. The feature should be **switchable per application**, since applications are independent authorization domains (ADR 0001, isolation principle) and one instance serves many.

Separately, clients and subscribers currently have no runtime way to learn whether a given application even does identity propagation, or which buckets and header to use. Today they must be told out of band. The assertion keys already live in a well-known KV location (ADR 0011); we want a parallel, discoverable descriptor per application, analogous to OIDC's `.well-known/openid-configuration`, that a client can download after connecting.

## Decision

### 1. Per-application toggle

Each application gets an `IdentityPropagation.Enabled` flag (see ADR 0007 for the model field).

- **Enabled (default true):** behavior is exactly ADR 0010 + 0011 — store the token, sign and store the assertion, grant keys-bucket read, and (for profiles with `AutoAddSessionAccess`) grant token-bucket read.
- **Disabled:** on authorization the callout performs **none** of that storage work for this application. It writes no token record and no assertion. Keys-bucket and discovery reads are still added to the application's grants (they are unconditional for every authenticated connection), so clients can verify assertions from other applications and read the descriptor that reports `enabled: false`. `AutoAddSessionAccess` on a profile of a disabled application is a config-validation error (nothing to read). Authorization, minting, and outcome events are unaffected.

The default is `true` to preserve the current design's behavior; disabling is the explicit opt-out.

### 2. Discovery KV

The callout publishes a small, secret-free descriptor per application to a discovery KV location, mirroring how it publishes assertion keys:

```
callout.apps.{app-slug}.discovery       # per-application discovery descriptor
```

- Written at startup and whenever config or the assertion key changes; the callout is the sole writer.
- **Readable by the application's authenticated connections** (read of `callout.apps.{app-slug}.discovery` is added to every profile grant in that application, like the keys bucket), never by the anonymous union grant.
- Contains no secrets or tokens — only names, the toggle state, and the current key id.

Descriptor shape (versioned JSON, ADR 0008 `schemaVersion` discipline):

```json
{
  "schemaVersion": 1,
  "app": "notes",
  "identityPropagation": {
    "enabled": true,
    "header": "Callout-Identity",
    "oidcTokenBucket": "callout_apps_notes_oidc_tokens",
    "assertionBucket": "callout_apps_notes_assertions",
    "assertionKeysBucket": "callout_assertion_keys",
    "currentKid": "2026-09-06a"
  }
}
```

The bucket names are the physical names defined by [ADR 0013](./0013-kv-bucket-naming-contract.md).

When identity propagation is disabled for the application, the descriptor is still published with `"enabled": false` and the bucket/kid fields omitted, so a client learns definitively that no assertion will exist and a subscriber learns not to expect a `Callout-Identity` header.

### Client and subscriber use

- **Client:** after connecting, `GET callout.apps.{app-slug}.discovery`; if `enabled`, perform the ADR 0010/0011 self-lookup (`GET callout.apps.{app-slug}.assertions.{jti}`) and attach the header. If disabled, skip identity entirely.
- **Subscriber:** `GET callout.apps.{app-slug}.discovery` to learn the header name, keys bucket, and current `kid`, then verify assertions per ADR 0011. If disabled, it knows messages from that application carry no callout identity.

## Consequences

- Identity propagation is opt-outable per application without touching any other application; disabling it removes all per-connection KV writes and the token-bucket grant for that app, while the unconditional keys and discovery reads remain.
- Clients and subscribers discover propagation behavior at runtime from within NATS, with no out-of-band configuration and no non-NATS endpoint. This parallels ADR 0011's in-NATS key discovery.
- The descriptor is public metadata (no secrets), readable by that application's authenticated connections, so it can safely be cached.
- Config validation gains a rule: `AutoAddSessionAccess` requires the application's `IdentityPropagation.Enabled` to be true.
- One extra discovery write per application at startup and on change; negligible compared to per-connection writes.
- The toggle's default (true) keeps existing deployments behaving as designed; operators opt out deliberately.

## Related

- Design: [07 — KV storage](../design/07-kv-storage.md), [06 — Identity propagation sequence](../design/06-identity-propagation-sequence.md), [04 — Configuration](../design/04-configuration.md), [05 — Runtime and lifecycle](../design/05-runtime-and-lifecycle.md).
- ADRs: [0010](./0010-session-jwt-store-in-kv.md) (token store this toggles), [0011](./0011-signed-identity-assertion.md) (assertion + keys discovery this parallels), [0007](./0007-internal-configuration-model.md) (the config field), [0008](./0008-publish-auth-outcome-events.md) (`schemaVersion` discipline).
