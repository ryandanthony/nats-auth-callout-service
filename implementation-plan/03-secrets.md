# P03 — Secret resolution

**Wave:** 1
**Depends on:** P02 (M0 gate)
**Blocks:** P13
**Estimated size:** 0.5–1 agent-day

## Scope

Implement `ISecretResolver` and the `SecretRef` kinds from doc 04: `inline`,
`file`, `k8sSecret`, `vault` — the first three fully, vault as a registered
kind that fails with a clear "not implemented" config error (doc 08: "File +
k8sSecret first; vault behind the same interface").

Grounding: `docs/design/04-configuration.md` (secrets section, ADR 0005),
`docs/design/05-runtime-and-lifecycle.md` (seeds loaded at Starting).

## Files owned

```
src/AuthCallout.Secrets/**
tests/AuthCallout.Secrets.Tests/**
```

## Tasks

1. `SecretResolver` dispatching on `SecretRef.Kind`:
   - `inline`: value from the ref itself (for dev only; log a warning).
   - `file`: read file content, trim trailing newline. Missing/unreadable file
     = fail closed with a path-naming error (never log the value).
   - `k8sSecret`: read via the **mounted-volume convention**
     (`/var/run/secrets/...` style path composition) rather than the k8s API,
     so `AuthCallout.Secrets` takes no KubernetesClient dependency and works
     in any pod without RBAC. Document the mount contract for P17 (Helm).
   - `vault`: typed `NotSupportedException` surfaced as config error.
2. Resolution is startup-time only (bootstrap layer is not hot-reloaded,
   doc 04); no caching/rotation machinery.
3. Hygiene: resolved values live in a `ResolvedSecret` wrapper that redacts
   `ToString()`, supports zeroing, and never appears in logs/exceptions.

## ADR→test map

| Behavior | Test |
|----------|------|
| Each kind resolves per contract | table tests, temp files |
| Missing file fails closed, message names path not value | assert |
| k8sSecret path composition | golden tests |
| Redaction: `ToString()`/interpolation leaks nothing | assert |
| vault ref → deterministic config error | assert |

## Definition of done

- [ ] All tests green; no secret value can be observed via logging test spy.
- [ ] Mount contract documented (consumed by P17).
- [ ] **Coordinator review passed** (acceptance criteria + ADR→test map verified
      against the diff; packet not accepted until then — see 00-overview.md).
