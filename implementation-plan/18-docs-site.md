# P18 — Docs site + operator documentation

**Wave:** 3 (after M2 so docs describe verified behavior)
**Depends on:** M2 (behavior proven), P16 (verify commands), P17 (install docs)
**Blocks:** M3
**Estimated size:** 1.5 agent-days

## Scope

User/operator documentation published to GitHub Pages at
`ryandanthony.github.io/nats-auth-callout-service/`, following the
[dottie docs](https://ryandanthony.github.io/dottie/) pattern (QUESTIONS A4).
Inspect the `ryandanthony/dottie` repo for the exact tooling (site generator,
workflow, theme) and replicate it rather than choosing a new stack.

## Files owned

```
docs-site/** (or the dottie-equivalent layout, e.g. docs/ + mkdocs.yml — match dottie)
.github/workflows/docs.yml
README.md (rewrite; P01's stub)
```

If the dottie pattern publishes from `docs/`, coordinate with the coordinator
on colocation with the existing `docs/design` + `docs/architecture-decision-log`
(keep those URLs stable; the site may include them as a "Design" section).

## Content

1. **Getting started**: what the service is (from design README), NATS server
   configuration for auth callout (`auth_callout` block, accounts, xkey),
   running via Docker, first application file, first connect.
2. **Configuration reference**: bootstrap + application fields generated
   from/checked against `schemas/config/*.json` (doc 09 is the source; the
   reference must not drift — add a CI check that every schema property
   appears in the reference page).
3. **Config sources guide**: files, Kubernetes CRD (install, RBAC, ownership
   labels, status), PostgreSQL (schema, NOTIFY).
4. **Authorization model guide**: applications, profiles, claim matching,
   template variables with the doc 02 worked example, anonymous union.
5. **Identity propagation guide**: the doc 06 sequence retold for service
   authors — "how to verify a `Callout-Identity` header in your subscriber",
   with code from the sample services; discovery descriptor; key rotation
   runbook (ADR 0014 add-and-promote/overlap/retire as operator steps).
6. **Operations**: health endpoints + probe semantics, metrics table, event
   schema + reason codes (doc 09), staleness/readiness behavior (ADR 0017),
   HA (replicas, queue group, shared secrets), Helm install (from P17),
   image verification (cosign, from P16).
7. **Troubleshooting**: deny-reason → likely-cause table, common
   misconfigurations (the P02 validator's error catalogue is the source).

## Definition of done

- [ ] Site builds in CI and deploys to GitHub Pages on main.
- [ ] Every page's claims spot-checked against the M2-verified behavior
      (no doc-only invention; cite design docs).
- [ ] Schema↔reference drift check in CI.
- [ ] README rewritten: badges, quick start, links to site + design docs.
- [ ] **Coordinator review passed** (acceptance criteria + ADR→test map verified
      against the diff; packet not accepted until then — see 00-overview.md).
