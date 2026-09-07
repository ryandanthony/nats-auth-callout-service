# P07 — Minter, callout responder, xkey encryption (+ ADR 0018)

**Wave:** 2
**Depends on:** P02; consumes P05/P08/P09 via interfaces (substitutable)
**Blocks:** P13
**Estimated size:** 1.5–2 agent-days

## Scope

The transport edge in `AuthCallout.Nats`: decode `$SYS.REQ.USER.AUTH`
requests, run the pipeline, mint/sign responses (NATS.Jwt + NKeys), bounded
parallel processing, queue-group subscription, **xkey encryption** (D14), and
authoring **ADR 0018** documenting the xkey decision.

Grounding: `docs/design/08-technical-architecture-csharp.md` (§Auth callout
implementation shape, §In-process parallelism, §Multiple instances),
`docs/design/03-resolution-pipeline.md` (mint and respond), doc 05 (signing
identity), D14 (xkey in scope), D18 (minimal JWT fields).

## Files owned

```
src/AuthCallout.Nats/Callout/**     (CalloutResponder, UserJwtMinter, CalloutCrypto)
tests/AuthCallout.Nats.Tests/Callout/**
docs/architecture-decision-log/0018-xkey-encrypted-callout.md
```

Do **not** touch `src/AuthCallout.Nats/Identity/**` (P08) or
`src/AuthCallout.Nats/Events/**` (P09). `NatsConnectionManager` belongs to
P13.

## Tasks

1. **`UserJwtMinter`** (`IUserJwtMinter`):
   - Granted → NATS user JWT: audience = target account, name, expiry,
     pub/sub allow lists — **nothing else** (D18: no bearer, no
     connection-type limits, no response perms). Signed with the issuer seed.
   - Denied → authorization response with an error string (generic, no
     internal detail leak; reason detail goes to events/logs only).
   - Both wrapped in the signed authorization response: subject = request
     `user_nkey`, audience = requesting server id (doc 03).
2. **`CalloutCrypto`** (`ICalloutCrypto`): xkey support per D14.
   - When `xkeySeed` configured: open (decrypt) incoming requests using the
     curve seed + the server's ephemeral public xkey from the request; seal
     responses back. `NATS.NKeys` curve (`XKey`) `Seal`/`Open`.
   - When not configured: pass-through no-op. Encrypted request received
     while unconfigured (or vice versa) → log + no reply (fail closed).
3. **`CalloutResponder`**: doc 08's shape verbatim —
   `SubscribeAsync<byte[]>("$SYS.REQ.USER.AUTH", queueGroup: "auth-callout")`
   driven by `Parallel.ForEachAsync` with `MaxDegreeOfParallelism` and linked
   per-request `CancelAfter(PerRequestTimeout)`. Flow per message:
   decrypt → decode (`NATS.Jwt` authorization request) → map to Contracts
   `AuthRequest` → `IResolutionPipeline` → on grant with propagation:
   `IIdentityStoreWriter.WriteAsync` **before** reply (doc 03) → mint →
   encrypt → reply → `IEventEmitter.EmitAsync` fire-and-forget off the
   critical path.
   - Identity-store write failure ⇒ deny (fail closed: doc 03 orders store
     before reply; a grant whose assertion can't be fetched is broken).
     Confirm with coordinator if this should instead degrade to
     grant-without-propagation. **Escalate, don't guess.**
   - Malformed/undecodable request → log, no reply.
4. **ADR 0018 — xkey-encrypted callout**: context (bearer tokens transit
   `$SYS.REQ.USER.AUTH`), decision (implement now, optional via bootstrap
   `xkeySeed`, all instances share the seed), consequences (secret
   distribution, Helm values, no plaintext fallback when configured). Follow
   the existing ADR format; update `docs/design/README.md` +
   `architecture-decision-log/README.md` indexes and mention in doc 08's
   HA requirements list.

## ADR→test map

| Behavior (source) | Test |
|-------------------|------|
| Minted JWT contains exactly the D18 fields, correct signer | decode with NATS.Jwt, golden assert |
| Response subject/audience binding (doc 03) | decode assert |
| Deny response carries no internal detail | assert |
| xkey round-trip: sealed request → open → sealed reply verifiable | key-pair fixture |
| Unconfigured xkey + encrypted request → no reply | test |
| Store-before-reply ordering (doc 03) | substitute store records call order vs reply |
| Store failure → deny (fail closed) | substitute throws |
| Per-request timeout cancels a hung pipeline; other requests unaffected | delayed substitute |
| Parallelism bounded at MaxDegreeOfParallelism | concurrency probe substitute |
| Queue-group name `auth-callout` pinned (doc 08) | config assert |
| Event emitted after reply, never blocks reply | ordering test |

Unit tests substitute `INatsConnection`-level surfaces; real-NATS coverage is
P14's.

## Definition of done

- [ ] All tests green.
- [ ] ADR 0018 committed, indexes updated.
- [ ] Store-failure semantics confirmed with coordinator (escalation above).
- [ ] Responder consumes only Contracts interfaces for pipeline/store/events.
- [ ] **Coordinator review passed** (acceptance criteria + ADR→test map verified
      against the diff; packet not accepted until then — see 00-overview.md).
