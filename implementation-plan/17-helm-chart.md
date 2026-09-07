# P17 — Helm chart

**Wave:** 3
**Depends on:** P16 (image), P13 (runtime contract: ports, probes, env), P11 (CRD), P03 (secret mount contract)
**Blocks:** M3
**Estimated size:** 1 agent-day

## Scope

A Helm chart under `deploy/helm/nats-auth-callout-service/` deploying the
service to Kubernetes with either config source, per QUESTIONS A3.

Grounding: doc 08 (health probes, multi-instance HA requirements, RBAC),
doc 04 (bootstrap layer shape), P03's secret mount contract, D21 (port 8080,
probe paths).

## Files owned

```
deploy/helm/nats-auth-callout-service/**
.github/workflows/* additions for chart lint (coordinator-mediated if touching ci.yml)
```

## Tasks

1. **Chart**: Deployment (default `replicas: 2` per doc 08 HA guidance),
   ConfigMap for the bootstrap file (doc 09 bootstrap schema), Secret mounts
   for issuer seed / service user seed / assertion keyset / optional xkey
   seed (paths matching P03's mount contract; support `existingSecret`
   references — the chart never generates key material), probes wired to
   `/startupz` `/healthz` `/readyz` on 8080 (D21), resources,
   `securityContext` (non-root, read-only rootfs per doc 08), OTel env
   (`OTEL_SERVICE_NAME`, `OTEL_EXPORTER_OTLP_ENDPOINT`) from values (D22),
   PodDisruptionBudget, optional ServiceMonitor stub (off by default).
2. **Source selection via values**:
   - `source.kind=files`: ConfigMap-or-volume of app files mounted at the
     configured directory.
   - `source.kind=k8s`: include P11's CRD install (values-gated
     `crds.install`, respecting Helm CRD conventions), ServiceAccount +
     ClusterRole/Binding from P11's RBAC, owner-label selector value.
   - `source.kind=postgres`: connection-string secret ref + pollInterval
     values.
3. **Validation**: `helm lint`, `helm template` golden tests (committed
   rendered fixtures for the three source kinds), and a P14-adjacent smoke:
   install into the Testcontainers k3s with the files source and assert the
   pod goes Ready (coordinate with P14 on fixture reuse; keep it a separate
   opt-in CI job if slow).
4. **README** for the chart: values table, three source-kind examples,
   secret-provisioning guidance (link ADR 0014 rotation flow).

## Definition of done

- [ ] `helm lint` + template goldens green in CI.
- [ ] k3s install smoke: pod Ready, `/readyz` 200, with the files source.
- [ ] All three `source.kind` values render valid manifests.
- [ ] No secret material in chart or values defaults.
- [ ] **Coordinator review passed** (acceptance criteria + ADR→test map verified
      against the diff; packet not accepted until then — see 00-overview.md).
