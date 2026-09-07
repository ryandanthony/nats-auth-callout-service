# P10 — Files config provider

**Wave:** 1
**Depends on:** P02 (M0 gate)
**Blocks:** P13, P14
**Estimated size:** 1 agent-day

## Scope

`IRoutingConfigProvider` over a watched directory of per-application
JSON/YAML files (`AuthCallout.Config.Files`).

Grounding: ADR 0005 (sources), ADR 0006 (atomic reload, keep last-good),
ADR 0017 (watch health/staleness), doc 04 (files source), doc 09 §3 (file
schema v1 + rule table).

## Files owned

```
src/AuthCallout.Config.Files/**
tests/AuthCallout.Config.Tests/Files/**
```

## Tasks

1. **Deserialization**: YAML (YamlDotNet) + JSON (System.Text.Json) into a
   source-local shape, then map → canonical `Application` (P02). **Strict**:
   unknown fields rejected (doc 09 rule table); `schemaVersion` missing →
   treated as 1 (migration window) with a warning; newer than supported →
   file rejected → whole batch rejected.
2. **Aggregation**: read all `*.yaml|*.yml|*.json` in the configured
   directory (non-recursive; pin and document), aggregate, run the P02
   validator, emit the batch. Any bad file rejects the whole batch
   (ADR 0006) with an error naming the offending file.
3. **Watching**: `FileSystemWatcher` + debounce (editors write multiple
   events) + periodic reconciliation relist (backstop for missed events,
   feeds `LastSuccessfulSync` per ADR 0017 — updated on every successful
   scan, not only on change). Watcher death → recreate with capped backoff;
   staleness surfaces through the provider's health state for P13's
   `config-source` check.
4. Emit validated batches through the provider contract; the provider does
   **not** decide keep-last-good (that's P13's config-apply loop), it reports
   "batch valid → here it is" / "batch invalid → error, no emit".

## ADR→test map

| Behavior (source) | Test |
|-------------------|------|
| doc 09 example file → expected canonical model | golden (shared fixture with P11/P12 contract test) |
| YAML and JSON same shape → identical model (ADR 0007) | test |
| Unknown field rejected; typo caught (doc 09) | test |
| schemaVersion missing/newer rules (doc 09) | table |
| One bad file rejects the batch, error names it (ADR 0006) | test |
| Add/remove file → app appears/disappears in next batch (doc 04) | temp-dir test |
| Debounce: N rapid writes → 1 batch | test |
| LastSuccessfulSync advances on no-change scans (ADR 0017) | test |

## Definition of done

- [ ] All tests green (real temp directories, no mocking of the FS).
- [ ] Golden canonical-model fixture published for the cross-source contract
      test (coordinate location with P11/P12 via coordinator).
- [ ] **Coordinator review passed** (acceptance criteria + ADR→test map verified
      against the diff; packet not accepted until then — see 00-overview.md).
