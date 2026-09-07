# P09 — Event emitter

**Wave:** 1
**Depends on:** P02 (M0 gate)
**Blocks:** P13 (wiring); P07 consumes via interface
**Estimated size:** 0.5 agent-day

## Scope

`IEventEmitter` in `AuthCallout.Nats`: best-effort auth outcome events per
ADR 0008 / doc 09 schema v1.

Grounding: ADR 0008, `docs/design/09-schemas-and-versioning.md` §1,
doc 05 (subjects resolution: per-app success/failure, global anonymous
subjects), C12 (wire enum + mapping from `DenyReason`).

## Files owned

```
src/AuthCallout.Nats/Events/**
tests/AuthCallout.Nats.Tests/Events/**
```

## Tasks

1. Build the v1 payload from the pipeline `Decision`: `schemaVersion: 1`,
   RFC 3339 UTC timestamp, `outcome`, `reason` via `DenyReasonMap`,
   optional `application`/`profile`/`principal`/`issuer`/`userNKey` per the
   doc 09 field table. **Secret-free**: never the raw token; principal is the
   `sub`/`azp`-style identifier only.
2. Subject selection (doc 05): selected app's `events.success`/`.failure`;
   anonymous decisions → bootstrap `anonymous.events`; app resolved but no
   subject configured → skip silently; failures before app selection
   (unknown issuer, invalid token with ambiguous candidates) → no app
   subject exists → anonymous/global failure subject if configured, else
   skip. Pin this fallback in code docs (doc 05's exact wording governs;
   if ambiguous, escalate to coordinator).
3. Best-effort: fire-and-forget with a swallow-and-count error policy
   (`events_publish_failures_total` metric, debug log). Never throws into the
   caller, never delays a reply (enforced by P07's ordering test too).

## ADR→test map

| Behavior (source) | Test |
|-------------------|------|
| Payload validates against `schemas/events/auth-outcome/v1.json` for every outcome/reason combination | contract table test |
| `anonymous` success reason code (doc 09) | test |
| `grant_construction_failed` covers required-var + unsafe-value (doc 09) | tests |
| Subject routing incl. unconfigured-subject skip (doc 05) | table |
| Publish failure swallowed + metered (ADR 0008 best-effort) | substitute throws |
| No token material in any payload | assert on fixtures |

## Definition of done

- [ ] All tests green; every emitted fixture schema-validates.
- [ ] Metric name recorded for P13's OTel wiring.
- [ ] **Coordinator review passed** (acceptance criteria + ADR→test map verified
      against the diff; packet not accepted until then — see 00-overview.md).
