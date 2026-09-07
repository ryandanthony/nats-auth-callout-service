# P11 — Kubernetes CRD config provider

**Wave:** 1
**Depends on:** P02 (M0 gate)
**Blocks:** P13, P14, P17 (CRD manifest)
**Estimated size:** 1.5–2 agent-days

## Scope

`IRoutingConfigProvider` over a cluster-wide watched CRD
(`AuthCallout.Config.K8s`), plus the CRD + RBAC manifests in `deploy/crd/`.

Grounding: ADR 0005, ADR 0006 (status write-back, atomic batch), ADR 0017
(watch loss), doc 04 (k8s source, label-selector ownership), doc 08 §K8s CRD
source, doc 09 §4 (CRD schema, `authcallout.ants.zone/v1 AuthApplication`,
status subresource).

## Files owned

```
src/AuthCallout.Config.K8s/**
tests/AuthCallout.Config.Tests/K8s/**
deploy/crd/authapplication-crd.yaml
deploy/crd/rbac.yaml
deploy/crd/samples/**            (Note.yaml, Task.yaml sample CRs)
```

## Tasks

1. **CRD manifest**: `authcallout.ants.zone/v1`, kind `AuthApplication`,
   namespaced, OpenAPI v3 validation schema mirroring
   `schemas/config/application/v1.json` (minus `schemaVersion`/`name`),
   `status` subresource enabled. Required fields enforced at admission
   (doc 09).
2. **RBAC**: `ClusterRole` (`get/list/watch` on `authapplications`, plus
   `patch` on `authapplications/status`), `ServiceAccount`,
   `ClusterRoleBinding` (doc 08).
3. **`CrdRoutingProvider`** (KubernetesClient):
   - List + watch **cluster-wide** with the instance's owner label selector
     (doc 04); CR spec → canonical `Application` (CR `metadata.name` = app
     name).
   - Any add/modify/delete → re-aggregate the full owned set → validate →
     emit batch (ADR 0006 atomicity).
   - **Watch resilience** (doc 08/ADR 0017): handle `410 Gone` by relist,
     reconnect with capped Polly backoff, periodic full relist updating
     `LastSuccessfulSync`; a silently dead watch surfaces as staleness, not
     as config loss.
   - `InClusterConfig()` with kubeconfig fallback (Aspire/dev, doc 08).
4. **Status write-back** (ADR 0006/doc 09): after each batch decision, patch
   each CR's `status`: `observedGeneration`, `accepted`, `reason` (validation
   error text for the offender; batch-rejected-by-sibling gets a distinct
   reason naming the offending CR). Status write failures are logged, never
   fatal.
5. **Testing strategy**: unit-test the provider against a faked
   watch-event stream (KubernetesClient abstracted behind a thin seam) for
   all relist/backoff/mapping logic; the real k3s + real CRD path runs in
   P14 (Testcontainers k3s is CI-acceptable per E24).

## ADR→test map

| Behavior (source) | Test |
|-------------------|------|
| doc 09 sample CR → identical canonical model as the equivalent file (ADR 0007) | shared golden contract test |
| Label selector: unowned CRs ignored (doc 04) | fake stream |
| Add/modify/delete each re-emit a full validated batch (ADR 0006) | fake stream |
| 410 Gone → relist; watch drop → backoff reconnect (ADR 0017) | fake stream |
| LastSuccessfulSync on relist without changes (ADR 0017) | test |
| Status accepted/rejected/sibling-rejected write-back (ADR 0006) | fake client asserts patches |
| CRD OpenAPI schema rejects malformed CR | k3s test in P14 (note here, owned there) |

## Definition of done

- [ ] Unit tests green without any cluster.
- [ ] CRD + RBAC manifests lint (`kubeconform` or `kubectl --dry-run=client`
      in a container).
- [ ] Sample CRs match doc 09 and are consumed by P14/P15.
- [ ] KubernetesClient referenced **only** by this project (doc 08 isolation).
