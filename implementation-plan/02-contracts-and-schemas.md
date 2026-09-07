# P02 — Contracts and schemas (the freeze point)

**Wave:** 0
**Depends on:** P01
**Blocks:** every Wave 1+ packet. Ends with the **human review gate (M0)**.
**Estimated size:** 1–2 agent-days

## Scope

Author `AuthCallout.Contracts` (canonical model + every cross-packet seam) and
the four JSON Schemas. After the M0 review, this project is **frozen**: any
change requires coordinator approval and a broadcast to affected packets.

Grounding: `docs/design/04-configuration.md` (canonical model, ADR 0007),
`docs/design/09-schemas-and-versioning.md` (field-level schemas, ADR 0015),
`docs/design/02-authorization-model.md` (concepts), `docs/design/07-kv-storage.md`
(bucket naming, ADR 0013), ADR 0016 (validation params), QUESTIONS C10–C13,
D12 (two enums + mapping), D14 (xkey), D15 (anonymous deny), D19 (`sub`
fallback).

## Files owned

```
src/AuthCallout.Contracts/**
tests/AuthCallout.Contracts.Tests/**
schemas/events/auth-outcome/v1.json
schemas/discovery/descriptor/v1.json
schemas/config/application/v1.json
schemas/config/bootstrap/v1.json
```

## Deliverables

### 1. Canonical config model (ADR 0007, doc 04)

Immutable records (`init`-only, value-equality) exactly mirroring doc 04's
canonical model:

- `BootstrapConfig`: `NatsUrl`, `IssuerSeed: SecretRef`,
  `ServiceUserSeed: SecretRef?`, `XkeySeed: SecretRef?` (**new**, D14),
  `AssertionKeys: AssertionKey[]?` (`Kid`, `Key: SecretRef`, `Active`),
  `Source` (kind + files/k8s/db settings + `WarnStaleness`, `MaxStaleness`,
  `ReconnectBackoff`), `Jwks` (`RefreshInterval`, `FetchTimeout`,
  `PrewarmRequired`), `Concurrency` (`MaxDegreeOfParallelism`,
  `PerRequestTimeout`, `QueueCapacity?`), `Anonymous` (`TargetAccount`,
  `Expiry`, `Events?`).
  - `Source` gains a `postgres` kind: `{ connectionString: SecretRef,
    pollInterval or LISTEN/NOTIFY channel }` — final shape decided here with
    P12's input **before** the freeze.
- `RoutingConfig`: `Applications: Application[]` + derived indexes doc 04
  describes (issuer → candidate apps; app-slug map).
- `Application`, `IssuerConfig`, `Profile`, `Grant`, `ClaimMatch`,
  `TemplateVariable`, `EventSubjects`, `IdentityPropagation` — fields per
  doc 04, including ADR 0016 validation fields with defaults
  (`ClockSkew` 60s cap 300s, `AllowedAlgorithms` asymmetric-only,
  `ValidateAudience` default true).
- `AppSlug` value type: the slugification rule (ADR 0013) lives here,
  validated at construction, so every packet slugs identically. Physical
  bucket-name derivation (`callout_apps_{slug}_oidc_tokens` etc.) as methods
  on it — single source of truth for ADR 0013 names.
- `ActiveConfig`: immutable snapshot pairing `BootstrapConfig` +
  `RoutingConfig` + a monotonic version stamp.
- **Validation**: an aggregate validator (used by every provider) enforcing
  doc 04's rules: unique names, unique slugs, `validateAudience` ⇒ audiences
  present, `autoAddSessionAccess` + propagation-disabled = error, algorithm
  allow-list rejects `HS*`/`none`, duration parsing/caps, template variables
  referenced by templates must exist, `required` semantics, etc. Deterministic
  error strings (they surface in CRD `status.reason` and logs).

### 2. Decision types

- `AuthRequest`: user NKey, raw token (nullable = anonymous), connect info,
  server id — decoupled from NATS.Jwt types so Core stays transport-free.
- `Decision` = `Granted | Denied` (discriminated union style):
  - `Granted`: target account, name, expiry, publish/subscribe lists,
    selected app + profile, propagation flags, generated-id slot.
  - `Denied`: `DenyReason` + detail.
- **`DenyReason` (internal enum)**: fine-grained (`NoToken`,
  `UnknownIssuer`, `SignatureInvalid`, `Expired`, `AudienceMismatch`,
  `AlgorithmRejected`, `NoAppMatch`, `AmbiguousApp`, `NoProfileMatch`,
  `RequiredVariableMissing`, `UnsafeTemplateValue`, `NameClaimError`,
  `AnonymousNoContributions` (D15), ...).
- **`EventReason` (wire enum)**: exactly doc 09's codes (`anonymous`,
  `invalid_token`, `no_issuer_match`, `no_app_match`, `ambiguous_app`,
  `no_profile_match`, `grant_construction_failed`, `granted`).
- **`DenyReasonMap`**: total function internal → wire, unit-tested for
  exhaustiveness (a new `DenyReason` without a mapping fails a test). Two
  enums + mapping per QUESTIONS C12.

### 3. Seam interfaces (QUESTIONS C11)

All async, all `CancellationToken`-accepting, XML-documented with the design
doc/ADR they implement:

| Interface | Implemented by | Consumed by |
|-----------|----------------|-------------|
| `IRoutingConfigProvider` (load/watch → validated `RoutingConfig` batches + `LastSuccessfulSync`) | P10, P11, P12 | P13 |
| `IActiveConfigSource` (current `ActiveConfig` snapshot + change notifications) | P13 | P05, P06, P08 |
| `ISecretResolver` | P03 | P13 |
| `ITokenValidator` (raw token + issuer → validated claims or `DenyReason`) | P06 | P05 |
| `IJwksCacheManager` (prewarm, refresh, single-flight; health surface) | P06 | P13 |
| `IResolutionPipeline` (`AuthRequest` + `ActiveConfig` → `Decision`) | P05 | P07, P13 |
| `IUserJwtMinter` (grant or denial → signed response JWT) | P07 | P07/P13 |
| `ICalloutCrypto` (xkey seal/open, no-op impl when unconfigured) | P07 | P07 |
| `IIdentityStoreWriter` (ensure buckets, write token/assertion/discovery/keys) | P08 | P07, P13 |
| `IAssertionSigner` (claims → compact JWT with `kid`; keyset from bootstrap) | P08 | P08 |
| `IEventEmitter` (decision → best-effort outcome event) | P09 | P07, P13 |
| `IServiceStateProvider` (Starting/Idle/Serving/Stopping + freshness substate) | P13 | health checks |
| Claims access: `IClaimsAccessor` over validated claims (nested paths, arrays) | P04 | P04, P05 |

Also: shared primitives `SecretRef` (`kind: inline|file|k8sSecret|vault` +
fields), `DurationString` parsing helper, `ConnectionId` generator contract
(high-entropy id, ADR 0010).

### 4. JSON Schemas (draft 2020-12)

Hand-authored, matching doc 09 field tables exactly:

- `schemas/events/auth-outcome/v1.json` — `additionalProperties: true`.
- `schemas/discovery/descriptor/v1.json` — `additionalProperties: true`;
  enabled/disabled variants (`if/then`).
- `schemas/config/application/v1.json` — **strict**
  (`additionalProperties: false`, doc 09 rule table).
- `schemas/config/bootstrap/v1.json` — strict; includes `xkeySeed`,
  `assertionKeys`, `source.kind: files|k8s|postgres`, `jwks`, `concurrency`,
  `anonymous`.

### 5. Tests (`AuthCallout.Contracts.Tests`)

- Validator table tests: every doc 04/ADR 0016 rule, accept + reject cases.
- Slug/bucket-name golden tests (ADR 0013 examples).
- `DenyReasonMap` exhaustiveness.
- Doc 09's example YAML/JSON payloads validate against their schemas
  (embed the doc examples as fixtures — drift between docs and schemas fails).
- Round-trip: example application file deserializes → canonical model →
  matches a hand-built expected instance.

## Definition of done

- [ ] Everything above compiles; `Contracts` references only BCL +
      `Microsoft.Extensions.Primitives`-level packages (no NATS/k8s/DB/HTTP).
- [ ] All tests green; schema fixtures match doc 09 examples byte-for-token.
- [ ] Each public type's XML doc cites its design doc/ADR.
- [ ] **Coordinator review, then Ryan review (M0 gate). Freeze announced.**
- [ ] P12's source-settings shape confirmed with coordinator before freeze.
