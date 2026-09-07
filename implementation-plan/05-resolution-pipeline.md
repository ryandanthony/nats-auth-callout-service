# P05 — Resolution pipeline

**Wave:** 2
**Depends on:** P02, P04 (matcher/templates), P06 interface only
**Blocks:** P07, P13
**Estimated size:** 1–1.5 agent-days

## Scope

`IResolutionPipeline` in `AuthCallout.Core`: the fixed
issuer → validate → app → profile → grant orchestration, plus the anonymous
composer. Pure decision logic — token validation is called through
`ITokenValidator` (substituted in tests), storage/events happen outside
(P07/P13 orchestrate around the decision).

Grounding: `docs/design/03-resolution-pipeline.md` (stages, deny points),
ADR 0003 (order), ADR 0004 (anonymous union), doc 02 (decision tree),
doc 03 grant-construction rules incl. auto-added KV reads, D15 (anonymous
zero-contributions = deny), D18 (minimal minted JWT fields).

## Files owned

```
src/AuthCallout.Core/Pipeline/**    (ResolutionPipeline, AppSelector, ProfileSelector, AnonymousComposer, GrantBuilder)
tests/AuthCallout.Core.Tests/Pipeline/**
```

## Tasks

1. **Stage orchestration** exactly per doc 03's flowchart, each deny mapped to
   its `DenyReason`:
   - No token → anonymous path.
   - Stage 1: read `iss` unvalidated → candidate apps from the config index;
     none → `UnknownIssuer`.
   - Stage 2: `ITokenValidator.ValidateAsync` once against the issuer;
     failure passes through the validator's `DenyReason`.
   - Stage 3: app `Match` over candidates; zero → `NoAppMatch`, >1 →
     `AmbiguousApp`; single candidate with no match and unique issuer → it.
   - Stage 4: first matching profile in order; none → `NoProfileMatch`.
2. **Grant construction** (doc 03): static subjects + expanded templates
   (P04 engine); name via nameClaim/`sub`; expiry = token `exp` (guaranteed
   present, ADR 0016); target account = app account. Auto-added reads
   (physical `$KV.` subjects via Contracts' bucket-name derivation):
   - `autoAddSessionAccess` → `$KV.callout_apps_{slug}_oidc_tokens.>`
   - every authenticated grant → `$KV.callout_assertion_keys.>` and
     `$KV.callout_apps_{slug}_discovery.descriptor` (regardless of toggle)
   - own-assertion read `$KV.callout_apps_{slug}_assertions.{jti}` when
     propagation enabled and jti (or `sub` fallback, doc 07) present.
3. **Anonymous composer** (ADR 0004): union of all `anonymousProfile` grants;
   **zero contributions → `Denied(AnonymousNoContributions)`** (D15); expiry
   from bootstrap `anonymous.expiry`; account = anonymous account; no KV
   reads ever.
4. **Decision output** carries everything downstream stages need (selected
   app/profile, propagation-enabled flag, jti-or-sub self-lookup key, claims
   needed for events) so P07/P08/P09 never re-derive.
5. Thread-safety: stateless per request over the immutable snapshot (doc 08
   parallelism prerequisite) — no mutable statics, verified by review.

## ADR→test map

| Behavior (source) | Test |
|-------------------|------|
| Every deny edge in doc 03's flowchart | one table row each |
| Resolution order fixed (ADR 0003): app match evaluated only post-validation | substitute validator asserts call order |
| Ambiguous app denies, no tie-break (doc 03) | test |
| First-match profile ordering (ADR 0001) | test |
| Anonymous union across apps (ADR 0004) + dedup | golden test |
| Anonymous zero contributions → deny (D15) | test |
| Auto-added KV read subjects exact (doc 03/07) | golden tests, toggle on/off |
| Expiry from token `exp` / anonymous from bootstrap | tests |
| doc 02 full decision-tree examples | end-to-end table over the pure pipeline |

## Definition of done

- [ ] All tests green; pipeline files ≥ 95% coverage (drives the Core 90% gate).
- [ ] `ITokenValidator` consumed via interface only (NSubstitute in tests).
- [ ] Decision object review with coordinator (P07/P08/P09 consume it).
- [ ] **Coordinator review passed** (acceptance criteria + ADR→test map verified
      against the diff; packet not accepted until then — see 00-overview.md).
