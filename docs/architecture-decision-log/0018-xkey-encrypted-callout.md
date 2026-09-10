# 0018 — xkey-encrypted callout

- **Status:** proposed
- **Date:** 2026-09-06
- **Deciders:** Ryan

## Context

`$SYS.REQ.USER.AUTH` carries every connection attempt's `connect_opts` in the
clear over NATS, including the client's raw bearer credential
(`connect_opts.auth_token` for the OIDC access token this service expects,
`connect_opts.pass`/`connect_opts.nkey` for other schemes). Anything able to
subscribe to that subject — another user sharing the callout's account, a
misconfigured permission, a future bug — reads every presented credential in
plaintext, even though the server denies non-`auth_users` publishers from
injecting a forged request or reply on the subject (that deny stops
forgery, not eavesdropping).

NATS server 2.10+ supports optional Curve25519 (`xkey`/NKey `Curve` prefix)
encryption of both the authorization request and response: the server seals
the request with its own ephemeral curve keypair against the auth service's
configured public xkey (`auth_callout.xkey` in config mode, or the account's
external-auth xkey in operator mode) and includes its ephemeral public key on
the request (`NatsServerId.XKey`, wire header `Nats-Server-Xkey`); the
service opens it with its own seed, and may seal its reply back to the
server's ephemeral public key the same way. This is orthogonal to account
isolation (running the callout in its own dedicated account, doc 08 §Why the
auth service runs in its own account) — isolation limits *who* can subscribe
to the subject at all, xkey protects the payload even against a subscriber
that is on the subject legitimately (e.g. an operator debugging with
`auth_users` access) or a leaked/misdirected subscription.

QUESTIONS D14 asks whether encrypted callout is in scope for v1. Answer:
implement it now, optional via a bootstrap secret, with this ADR documenting
the decision.

## Decision

xkey encryption is **implemented and optional**, gated by a new bootstrap
secret reference: `xkeySeed: SecretRef?` (a Curve/`X`-prefixed NKey seed).

- **Configured (`xkeySeed` set):** the responder **opens** every incoming
  request with this instance's curve keypair and the server's ephemeral
  public key carried on the request (`NatsAuthorizationRequest.Server.XKey`
  when the request itself decodes as plaintext JSON claims — see below for
  how the *wire* payload signals encryption), then **seals** its reply back
  to that same server ephemeral public key before publishing. All instances
  in a queue group share the same `xkeySeed` (doc 08 §Multiple instances:
  "every instance needs the same xkey seed to decrypt requests"), the same
  way they already share the issuer seed.
- **Not configured (`xkeySeed` null):** the responder is a pass-through
  no-op — it never attempts to seal or open, exactly mirroring the
  pre-existing, unencrypted callout behavior. This is the default; nothing
  changes for a deployment that never sets `xkeySeed`.
- **Mismatch is fail-closed, not fail-open.** An encrypted request arriving
  while unconfigured, or a plaintext request arriving while xkey *is*
  configured and the server's `auth_callout.xkey` was expected to encrypt,
  is logged and **dropped without a reply** rather than processed as if
  encryption were absent. The client experiences the normal auth-callout
  timeout-then-reject behavior (doc 05: same fail-closed posture as every
  other unrecoverable request-level problem), never a silently-downgraded
  plaintext round-trip. Detecting "is this request encrypted" does not
  require guessing content shape: the server always signals encryption via
  the `Nats-Server-Xkey` message header (present ⇒ ciphertext payload,
  absent ⇒ plaintext claims payload), so the responder branches on header
  presence, not on attempting to parse-then-fallback.
- **Curve seal/open**, not raw seed material: implemented via `NATS.NKeys`'
  `KeyPair` (`PrefixByte.Curve`) `Seal(payload, recipientPublicKey)` /
  `Open(ciphertext, senderPublicKey)`, which is the NaCl-box-compatible
  X25519 + XSalsa20-Poly1305 construction the NATS server itself uses — this
  service never reimplements the cryptographic primitives.
- **`ICalloutCrypto`** (Contracts, P02) is the seam: a real implementation
  wraps a resolved `xkeySeed`; a no-op implementation is used when
  unconfigured. The responder (P07) always calls through the interface and
  never branches on configuration itself (`IsEnabled` exists only for the
  responder's own encrypted-while-unconfigured fail-closed check, not for
  choosing which crypto path to run).

## Consequences

- **Secret distribution.** `xkeySeed` joins `issuerSeed`/`serviceUserSeed` as
  a bootstrap secret every instance in a queue group must share (P03's
  `k8sSecret` mounted-volume convention, P17's Helm chart mounts it the same
  way as the other seeds — no new mount mechanism, just one more optional
  `SecretRef`).
- **Helm values.** P17 gets an optional `xkeySeed` secret-ref value,
  defaulting to unset (encryption off), matching this ADR's optional-by-
  default posture; the chart never generates key material for it, same as
  the other seeds.
- **No plaintext fallback when configured.** Once `xkeySeed` is set,
  the responder never silently accepts a plaintext request or sends a
  plaintext reply — the fail-closed behavior above is unconditional, so a
  deployment that turns xkey on gets a hard guarantee, not a best-effort
  upgrade path. If an operator needs to roll xkey out gradually, that is a
  server-side `auth_callout.xkey` config change coordinated with this
  service's `xkeySeed`, not a soft transition inside this service.
- **No new external dependency.** `NATS.NKeys`' `KeyPair` already covers
  `PrefixByte.Curve` seal/open; no additional crypto package is pulled in.
- **HA unaffected beyond the shared-secret requirement** already noted in
  doc 08 §Multiple instances, which this ADR is the authority for (doc 08
  cross-references here for the *why*).

## Related

- Design: [08 — Technical architecture](../design/08-technical-architecture-csharp.md#multiple-instances-ha-and-exactly-once-handling) (§Multiple instances "Shared xkey" requirement), [03 — Resolution pipeline](../design/03-resolution-pipeline.md#mint-and-respond) (mint and respond, wrapped by this ADR's seal/open on the wire).
- ADRs: [0005](./0005-config-abstraction-and-sources.md) (secret references, the mechanism `xkeySeed` reuses).
