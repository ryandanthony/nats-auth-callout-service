# P08 — Identity stores + assertion signing

**Wave:** 2
**Depends on:** P02 (M0 gate)
**Blocks:** P13; P07 consumes via interface
**Estimated size:** 1.5–2 agent-days

## Scope

Everything ADR 0010–0014 at runtime: `IAssertionSigner` (in
`AuthCallout.Identity`) and `IIdentityStoreWriter` (in `AuthCallout.Nats`) —
bucket provisioning, token/assertion writes with per-message TTL, keys bucket
publication + rotation, discovery descriptors.

Grounding: `docs/design/07-kv-storage.md` (**authoritative** for
keys/values/readers/TTLs), doc 06 (sequence), ADR 0010 (token store),
ADR 0011 (assertion), ADR 0012 (toggle + discovery), ADR 0013 (physical
naming), ADR 0014 (keyset rotation), doc 08 §KV, D16 (callout creates
buckets), D17 (TTL = token exp, no backstop), D20 (no separate schema doc for
the assertion).

## Files owned

```
src/AuthCallout.Identity/Assertions/**   (AssertionSigner, keyset)
src/AuthCallout.Nats/Identity/**         (IdentityStoreWriter, BucketProvisioner, DiscoveryPublisher, KeysPublisher)
tests/AuthCallout.Identity.Tests/Assertions/**
tests/AuthCallout.Nats.Tests/Identity/**
```

## Tasks

1. **`AssertionSigner`** (pure, no I/O): compact JWT with claims `id`, `sub`,
   `app`, `iss`, `exp` (aligned to token lifetime) + `schemaVersion`, `kid`
   header from the keyset's **active** key (Ed25519 via NATS.NKeys). Keyset
   invariants (exactly one active) are validated by P02; the signer consumes
   the resolved keyset. Verification helper included (used by tests, P14, and
   sample services).
2. **`ConnectionIdGenerator`**: high-entropy opaque id (ADR 0010); not a
   token claim. 128-bit+ from `RandomNumberGenerator`, URL/subject-safe
   encoding (no `.`/`*`/`>`).
3. **`BucketProvisioner`** (D16): at config-apply, ensure buckets exist for
   every propagation-enabled app (`callout_apps_{slug}_oidc_tokens`,
   `callout_apps_{slug}_assertions`) plus global `callout_assertion_keys` and
   per-app `callout_apps_{slug}_discovery`. Per-message TTL enabled
   (`AllowMsgTTL`, server 2.11+); **no MaxAge backstop** (D17). App removed
   from config → bucket left orphaned, warn log + `orphaned_buckets` metric.
   Idempotent (CreateOrUpdate semantics).
4. **`IdentityStoreWriter`** per-authorization (doc 03 §store): generate id,
   write raw OIDC token at `{id}` with TTL = `exp - now`, sign assertion,
   write at `{jti}` (fallback `sub` when jti absent, doc 07; neither → skip
   both writes, propagation unavailable, grant unaffected). Skip everything
   when app propagation disabled or anonymous.
5. **`KeysPublisher`** (ADR 0011/0014): at startup and rotation, write every
   keyset public key to `callout_assertion_keys/{kid}` + `current` pointer.
   Rotation = bootstrap change = restart (keyset is bootstrap config); old
   kids stay readable.
6. **`DiscoveryPublisher`** (ADR 0012): per app at startup/config change,
   write the doc 09 descriptor (enabled variant with header name
   `Callout-Identity`, physical bucket names, current kid; or
   `enabled: false` variant) to `callout_apps_{slug}_discovery/descriptor`.
   Payload must validate against `schemas/discovery/descriptor/v1.json`.

## ADR→test map

| Behavior (source) | Test |
|-------------------|------|
| Assertion claims + kid exactly per ADR 0011 | decode + golden |
| Assertion verifiable with published public key; tampered fails | round-trip |
| Physical bucket/key names for every logical path (ADR 0013 / doc 07 table) | golden table |
| Token TTL = exp − now exactly, no clamp (D17) | mock KV asserts TTL |
| jti → sub fallback → skip (doc 07) | table |
| Toggle off: no token/assertion writes, discovery says enabled:false (ADR 0012) | table |
| Keys bucket never receives a private key (ADR 0011) | assert payloads |
| current pointer + per-kid entries on rotation (ADR 0014) | test |
| Discovery payload validates vs schema, both variants (doc 09) | contract test |
| Provisioner idempotent; orphan warn metric (D16) | test |

Unit tests run against an in-memory `INatsKVStore` substitute; real-JetStream
verification is P14's.

## Definition of done

- [ ] All tests green; discovery/assertion payloads schema-validated.
- [ ] Bucket-name derivation comes from Contracts (no local string building).
- [ ] Verification helper exposed for P14/P15 reuse.
- [ ] **Coordinator review passed** (acceptance criteria + ADR→test map verified
      against the diff; packet not accepted until then — see 00-overview.md).
