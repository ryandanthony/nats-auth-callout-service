# P14 — Integration suite (Testcontainers)

**Wave:** 3
**Depends on:** P13 (+ P11, P12 for their scenarios)
**Blocks:** M2
**Estimated size:** 2 agent-days

## Scope

`tests/AuthCallout.Integration`: the real-dependencies proof. Testcontainers
for `nats:2.11` (JetStream + auth callout configured), **real Keycloak**
(E23), **k3s** (E24), and PostgreSQL. This suite is what turns "components
green" into **M2: first end-to-end auth**.

Grounding: doc 08 §Validation/feedback loop, doc 06 (the full identity
sequence to reproduce), all ADR behaviors that need a real server (per-message
TTL, queue groups, KV permissions).

## Files owned

```
tests/AuthCallout.Integration/**
```

## Fixtures

- **NatsFixture**: `nats:2.11 -js` with a generated server config enabling
  `auth_callout` (issuer account NKey, auth user, xkey variants), accounts
  for apps + anonymous. Config templating helper owned here.
- **KeycloakFixture** (Testcontainers.Keycloak): realm import with two
  clients (user password grant + service client-credentials), roles,
  custom claims (`household_id`-style) to exercise templates.
- **StubIdp** (from P06): for fast token-shape edge cases where Keycloak
  can't easily produce them (bad alg, crafted headers).
- **K3sFixture** (Testcontainers.K3s): applies P11's CRD + RBAC + sample CRs.
- **PostgresFixture** (Testcontainers.PostgreSql): P12 migrations + seed rows.
- Collection fixtures share containers across tests; suite target < ~10 min.

## Scenarios (each cites what it proves)

1. **E2E happy path (M2 definition)**: Keycloak user token → client connects
   → granted exactly the profile's subjects (positive + negative pub/sub
   probes) → KV token/assertion records present with correct TTL → outcome
   event received → client fetches own assertion → publishes with
   `Callout-Identity` header → subscriber (with `autoAddSessionAccess`)
   verifies assertion via keys bucket and reads the OIDC token — the full
   doc 06 sequence.
2. Service principal via client-credentials + `azp` profile (ADR 0002).
3. Anonymous: union grant across two apps (ADR 0004); zero contributions →
   denied (D15).
4. Deny matrix over the wire: invalid signature, expired, wrong audience,
   unknown issuer, no profile → connection refused + failure event with the
   right reason code (doc 09).
5. KV access enforcement (doc 07): without `autoAddSessionAccess` token
   bucket read is denied; client can read only its own assertion entry;
   anonymous cannot read keys/discovery buckets.
6. Per-message TTL: short-lived token → record expires ≈ token exp (D17,
   real JetStream).
7. Config hot-reload (files source): edit file → new connection gets new
   permissions, existing connection keeps old (ADR 0006); bad file → last
   good kept + `/readyz` still 200; staleness thresholds → 503 (ADR 0017,
   clock-compressed settings).
8. CRD source on k3s: CR applied → app served; malformed CR rejected by
   OpenAPI schema; CR status write-back accepted/rejected (P11 handoff);
   watch survives API-server restart (relist).
9. Postgres source: seeded rows served; UPDATE + NOTIFY → reload; poll
   fallback with LISTEN broken (P12 handoff).
10. Queue group: two callout instances, N connects → each auth handled once
    (event count == connect count), kill one instance mid-load → all
    connects still succeed (doc 08 HA).
11. xkey: server configured with callout xkey + service holding the seed →
    auths succeed; wrong/missing seed → fail closed (ADR 0018).
12. Health over real lifecycle: `/startupz` gates on prewarm, `/readyz`
    flips with NATS container stop/start.
13. Multi-instance skew: reload while both instances serve (doc 08: both
    verdicts valid during convergence) — smoke only.

## Definition of done

- [ ] All scenarios green locally and in the CI `integration` job.
- [ ] Suite runtime bounded (shared fixtures, parallel collections where safe).
- [ ] M2 report to coordinator with evidence (scenario list + timings).
- [ ] Flake policy: any retry-decorated test documents why.
