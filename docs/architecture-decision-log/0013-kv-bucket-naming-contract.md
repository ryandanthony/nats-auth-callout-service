# 0013 — KV bucket naming contract

- **Status:** proposed
- **Date:** 2026-09-06
- **Deciders:** Ryan

## Context

The design and [ADR 0010](./0010-session-jwt-store-in-kv.md), [ADR 0011](./0011-signed-identity-assertion.md), and [ADR 0012](./0012-identity-propagation-toggle-and-discovery.md) describe the identity stores using dotted, subject-style paths:

```
callout.apps.{app-slug}.oidc-tokens.{id}
callout.apps.{app-slug}.assertions.{jti}
callout.assertion-keys.{kid}
callout.apps.{app-slug}.discovery
```

These read naturally as NATS subjects, but a JetStream Key-Value **bucket name is not a subject**. A KV bucket maps to a stream named `KV_<bucket>` whose subjects are `$KV.<bucket>.<key>`, and the **bucket name is constrained to `[A-Za-z0-9_-]+`** — dots are not allowed. Keys, by contrast, are subject tokens and *may* contain dots. So the dotted paths above conflate two different things: the **bucket** (a storage container) and the **key** (an entry within it).

This split is not an implementation detail. The bucket and key names are a **consumer-facing interface**: every service that reads the OIDC token store, fetches its own assertion, reads the keys bucket, or reads the discovery descriptor must construct the exact same bucket and key names the callout wrote. If the callout and its consumers disagree on the mapping, identity propagation silently breaks. It must be pinned once and versioned like any other contract.

## Decision

Define an explicit, deterministic mapping from each logical store to a **(bucket name, key)** pair. The dotted paths in the other ADRs are the *logical* address; this ADR is the *authoritative physical* mapping.

### Naming rules

- **Bucket names** match `^[A-Za-z0-9_-]+$` (JetStream constraint). We use `snake_case` with a fixed `callout_` prefix and no dots.
- **Keys** are subject tokens; dots are allowed and are used only where a key is naturally hierarchical. Every dynamic key segment (`app-slug`, `id`, `jti`, `kid`) is first passed through the **key-safety transform** below.
- The `{app-slug}` is folded into the **bucket name**, not the key, so per-application isolation is enforced at the bucket boundary (a grant to one app's bucket cannot read another's).

### Mapping table

| Logical store (per ADR) | Bucket name | Key | Value |
|--------------------------|-------------|-----|-------|
| OIDC token store (0010) | `callout_apps_{app-slug}_oidc_tokens` | `{id}` | client OIDC access token (JWT) |
| Id-lookup / assertions (0010, 0011) | `callout_apps_{app-slug}_assertions` | `{jti}` | client's signed identity assertion |
| Assertion keys (0011) | `callout_assertion_keys` | `{kid}` | assertion verification public key |
| Assertion keys — current pointer (0011, 0014) | `callout_assertion_keys` | `current` (reserved) | the active `kid` |
| Discovery descriptor (0012) | `callout_apps_{app-slug}_discovery` | `descriptor` | per-app discovery JSON (secret-free) |

`current` is a **reserved key** in the assertion-keys bucket: it holds the pointer to the active signer's `kid` (ADR 0011, ADR 0014). Consequently `current` is a **forbidden `kid` value** — config-load validation rejects any keyset entry whose `kid` is `current` (fail closed), so the pointer can never collide with a per-key entry.

`{app-slug}` is the stable, collision-free application slug validated at config load ([ADR 0010](./0010-session-jwt-store-in-kv.md)); the same slug value is reused verbatim in the bucket name.

### App-slug and key-safety transform

To guarantee both bucket-name and key validity, dynamic segments are sanitized with a single documented rule, applied at config load for slugs and at write time for ids/jti/kid:

- Lowercase.
- Replace any run of characters outside `[a-z0-9-]` with a single `-` (for slugs) or `_` (inside bucket names).
- Trim leading/trailing separators.
- Reject (fail closed) if the result is empty, exceeds the length budget, or collides with another application's slug (validated per ADR 0010).

`{id}` is a callout-generated GUID and `{kid}` is a key id the callout controls, so both are already safe; the transform is a defensive backstop. `{jti}` (and its `sub` fallback) comes from the token and **must not** be used sanitized-only: sanitization is many-to-one (`User.A`, `user_a`, and `USER A` all sanitize to `user_a`), so distinct principals could alias each other's entries. Token-supplied key segments therefore use the **key derivation**: `sanitize(raw) + "_" + first-16-hex(SHA-256(raw))` (`AppSlug.DeriveKeySegment`), which keeps the segment operator-readable while making distinct raw values yield distinct keys. Both the granted self-lookup subject and the store-writer's actual key derive through the same function, so they can never diverge.

### Grant subjects follow the physical mapping

NATS permission grants reference the underlying KV subjects, `$KV.<bucket>.<key>`. Therefore:

- **Token read (`AutoAddSessionAccess`):** read `$KV.callout_apps_{app-slug}_oidc_tokens.>`.
- **Assertion self-lookup:** read `$KV.callout_apps_{app-slug}_assertions.{own-jti}` (single key, not `>`).
- **Keys read:** read `$KV.callout_assertion_keys.>`.
- **Discovery read:** read `$KV.callout_apps_{app-slug}_discovery.descriptor`.

The minter builds these subjects from the same mapping function the store writer uses, so grants and writes can never diverge.

### JetStream wire-level subjects a real KV read requires

The `$KV.<bucket>.<key>` subject above is the *logical* KV grant, but a real JetStream KV
client (`NatsKVContext`) does not read entries by subscribing to that subject — it issues
JetStream API requests against the bucket's backing stream (`KV_<bucket>`) and receives the
replies on its connection inbox. So for every bucket a grant can read, the minter also adds:

- **Publish** `$JS.API.STREAM.INFO.KV_<bucket>` and `$JS.API.DIRECT.GET.KV_<bucket>` — scoped
  to that single backing stream, never the account-wide `$JS.API.>`, so a grant reaches only
  the buckets it was granted `$KV` read on.
- **Subscribe** `_INBOX.{app-slug}.{derived-principal-key}.>` (once per grant that reads at
  least one bucket) — the request-reply path for those JetStream API calls, **scoped to the
  connection's own principal** (`sub`, falling back to `azp`, then the minted name; derived
  via the key derivation above). The account-wide `_INBOX.>` is deliberately **not** granted:
  it would let every authenticated client subscribe to every other client's request replies
  in the account (verified against a real server — `_INBOX` has no server-side ownership
  semantics; it is only a client-side naming convention). The client must configure the
  matching custom inbox prefix (`NatsOpts.InboxPrefix` in NATS.NET, `custom_inbox_prefix`
  in other clients): `_INBOX.{app-slug}.{derived-principal-key}`, computable client-side
  from the app slug and its own token. Connections sharing a principal share a prefix
  (same identity); everyone else's inbox is out of reach.

These are auto-added by `GrantBuilder` alongside the `$KV.*` reads (they are not something
application config must specify), keeping the logical model in config and the wire-level
plumbing in the callout. Without them the read sequence in [doc 06](../design/06-identity-propagation-sequence.md)
cannot complete for a real client.

A **responder** service (one that answers NATS request-reply on an app subject) additionally
needs to *publish* to its requesters' reply subjects. That is a profile-config concern, not
an auto-grant: give the responder profile `publish: ["_INBOX.{app-slug}.>"]`. Publishing to
an inbox is a far weaker capability than subscribing to one (it can send a reply, not read
anyone's traffic), so the app-wide publish grant is acceptable where the app-wide subscribe
grant was not.

### Single source of truth

The mapping is implemented **once** as a pure function (`BucketNaming` in `AuthCallout.Contracts`) shared by the store writer, the grant minter, and the discovery-descriptor emitter. The discovery descriptor ([ADR 0012](./0012-identity-propagation-toggle-and-discovery.md)) publishes the resolved bucket and key names at runtime, so consumers **discover** them rather than re-deriving the rule, keeping this ADR the authority for the callout side while the descriptor is the authority for consumers.

## Consequences

- Dotted logical paths in ADRs 0010–0012 are documentation-level addresses; the physical bucket/key split is defined here and referenced by them.
- Per-application isolation is enforced at the bucket boundary, which is stronger than key-prefix isolation and maps cleanly to NATS subject grants.
- The discovery descriptor removes the need for consumers to hard-code the naming rule; only the descriptor field names remain a fixed contract.
- Changing the mapping is a breaking change for existing consumers and must bump the discovery descriptor `schemaVersion` (and, if the shape changes, the CRD/event schema versions).
- A shared `BucketNaming` function is the only place bucket/key strings are formed; tests assert writer, minter, and descriptor all agree.
- `{jti}` key derivation is mandatory: an unsanitized token value could otherwise produce an invalid key, and a sanitized-only value could collide across principals (aliasing one client's assertion entry to another's).

## Related

- Design: [04 — Configuration](../design/04-configuration.md) (app slug), [05 — Runtime and lifecycle](../design/05-runtime-and-lifecycle.md), [07 — KV storage](../design/07-kv-storage.md) (authoritative bucket reference), [08 — Technical architecture (C#)](../design/08-technical-architecture-csharp.md) (KV mapping notes).
- ADRs: [0010](./0010-session-jwt-store-in-kv.md) (token store and app slug), [0011](./0011-signed-identity-assertion.md) (assertion + keys bucket), [0012](./0012-identity-propagation-toggle-and-discovery.md) (discovery descriptor that publishes the resolved names).
