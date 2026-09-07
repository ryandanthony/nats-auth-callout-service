# P04 — Claim matcher + template engine

**Wave:** 1
**Depends on:** P02 (M0 gate)
**Blocks:** P05
**Estimated size:** 1–2 agent-days

## Scope

The pure-decision building blocks in `AuthCallout.Core`: claim access, claim
matching, and the templated-subject-variable engine. No I/O, no NATS types.
This is the highest-test-density packet; it carries much of the 90% Core
coverage bar.

Grounding: `docs/design/02-authorization-model.md` (claim match semantics,
template variables), ADR 0009 (templated subject variables), ADR 0001/0002
(what matching selects), D19 (`nameClaim` → `sub` fallback).

## Files owned

```
src/AuthCallout.Core/Claims/**       (ClaimsAccessor)
src/AuthCallout.Core/Matching/**     (ClaimMatcher)
src/AuthCallout.Core/Templates/**    (transforms, variable resolver, expander)
tests/AuthCallout.Core.Tests/Claims/**
tests/AuthCallout.Core.Tests/Matching/**
tests/AuthCallout.Core.Tests/Templates/**
```

Do **not** touch `src/AuthCallout.Core/Pipeline/**` (P05).

## Tasks

1. **`ClaimsAccessor`** (implements `IClaimsAccessor`): nested path traversal
   (`realm_access.roles`), scalar/array/absent distinction, string coercion
   rules pinned and documented (numbers/bools → invariant strings; objects →
   no value).
2. **`ClaimMatcher`**: doc 02 semantics exactly — OR within a key's value
   list (array claim matches if it contains any listed value), AND across
   keys, empty/absent match handling (validity of empty match is a config
   concern, P02 validator; the matcher just evaluates).
3. **Transform pipeline** (ADR 0009): `lowercase`, `replace(a -> b)`,
   `sanitize`, `hash`, `prefix(x)`/`suffix(x)`, `require-safe`. Pin exact
   semantics in code docs:
   - `sanitize`: replace NATS-special chars (`.`, `*`, `>`, whitespace, and
     any char outside a safe set `[A-Za-z0-9_-]`) with `_`.
   - `hash`: SHA-256, lowercase hex, first 16 chars (stable across restarts).
   - `require-safe`: deny (UnsafeTemplateValue) if value still contains
     NATS-special/unsafe chars.
   - Ordered application; parse errors are config-load errors (P02 validator
     owns the grammar — coordinate the transform-spec parser location with
     P02 before the freeze: parser in Contracts, execution here).
4. **Variable resolver + template expander**: resolve each
   `TemplateVariable` from claims (multi-valued claims → value sets),
   `required` + no value → `RequiredVariableMissing`; expand
   `publishTemplates`/`subscribeTemplates` per-value with **cross-product**
   across multiple multi-valued variables; variable with no value → template
   emits nothing; unknown `{name}` in a template is a config error (P02).
   Deterministic output ordering (sorted) for testability.
5. **Name resolution**: profile `nameClaim` → value, absent → fall back to
   `sub` (D19); `sub` also absent → `NameClaimError` deny.

## ADR→test map

| Behavior (source) | Test |
|-------------------|------|
| OR-within-key, AND-across-keys (doc 02) | table tests |
| Nested + array claims (doc 02) | table tests |
| Every transform, each edge (ADR 0009) | per-transform tables |
| doc 02 worked example: `household_id=[H1,H2]`, `email=Alice@Example.com` → 2 subjects | golden test |
| Cross-product expansion (ADR 0009) | table tests |
| required-missing → deny; optional-missing → emits nothing (ADR 0009) | table tests |
| unsafe value + require-safe → deny (ADR 0009) | table tests |
| nameClaim fallback chain (D19) | table tests |

## Definition of done

- [ ] All tests green; this packet's files at ≥ 95% line coverage.
- [ ] Transform semantics documented in XML docs (interop contract).
- [ ] No references beyond Contracts + BCL.
