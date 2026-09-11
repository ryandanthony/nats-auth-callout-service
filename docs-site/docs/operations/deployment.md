---
sidebar_position: 3
---

# Deployment

## Helm chart

```bash
helm install auth-callout deploy/helm/nats-auth-callout-service \
  --set source.kind=files \
  --set secrets.issuerSeed.existingSecret=authcallout-issuer-seed
```

Defaults: `replicaCount: 2` (HA queue-group), a `PodDisruptionBudget`
(`minAvailable: 1`), non-root `securityContext` with a read-only root
filesystem, probes wired to `/startupz` `/healthz` `/readyz` on `8080`.
The chart **never generates key material** — every seed is an
`existingSecret` reference the operator provisions out of band.

### Values by source kind

**Files** (`source.kind: files`):

```yaml
source:
  kind: files
  files:
    directory: /config/apps
    configMap:
      create: true         # or false + existingConfigMap: <name>
      files:
        notes.yaml: |
          schemaVersion: 1
          name: notes
          ...
```

**Kubernetes CRD** (`source.kind: k8s`):

```yaml
source:
  kind: k8s
  k8s:
    labelSelector: "authcallout.ants.zone/owner=prod"
    crds:
      install: true   # installs deploy/crd/authapplication-crd.yaml
    rbac:
      create: true    # ServiceAccount + ClusterRole/ClusterRoleBinding
```

**PostgreSQL** (`source.kind: postgres`):

```yaml
source:
  kind: postgres
  postgres:
    connectionString:
      existingSecret: authcallout-db
      key: connectionString
    pollInterval: 5s
    notifyChannel: authcallout_config_changed
```

See the [config sources guide](../configuration/sources/overview) for what
each source actually does at runtime.

### Secret provisioning

| Values key | Required | Secret contents |
|------------|----------|------------------|
| `secrets.issuerSeed` | yes | The issuer signing seed. |
| `secrets.serviceUserSeed` | no (`enabled: false` by default) | The callout's dedicated NATS service-user seed. |
| `secrets.xkey` | no (`enabled: false` by default) | Curve/xkey seed for encrypted callout traffic ([ADR 0018](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0018-xkey-encrypted-callout.md)). |
| `secrets.assertionKeys` | no (empty by default) | A list of `{ kid, existingSecret, key, active }` — the assertion signing keyset. See the [key rotation runbook](../guides/identity-propagation#key-rotation-runbook) before editing this in place. |

Every entry references an **existing** Kubernetes `Secret`
(`kubectl create secret generic authcallout-issuer-seed --from-file=seed=./issuer.seed`)
— the chart mounts it and never creates key material itself.

### OTel

```yaml
otel:
  serviceName: "nats-auth-callout-service"
  otlpEndpoint: "http://otel-collector.observability:4317"
```

Maps directly to `OTEL_SERVICE_NAME` / `OTEL_EXPORTER_OTLP_ENDPOINT` — see
[Health & observability](./health-and-probes#metrics).

### Golden tests

The chart's `helm template` output is validated against committed fixtures
for all three `source.kind` values in CI (`helm lint` + template goldens);
see [`deploy/helm/nats-auth-callout-service`](https://github.com/ryandanthony/nats-auth-callout-service/tree/main/deploy/helm/nats-auth-callout-service).

## Container image

Multi-arch (`linux/amd64`, `linux/arm64`) images are built on
`mcr.microsoft.com/dotnet/runtime` (framework-dependent, trimmed
self-contained — no ASP.NET/MVC base needed) and published to GHCR on every
tagged release:

```bash
docker pull ghcr.io/ryandanthony/nats-auth-callout-service:latest
```

The image runs as a **non-root** user (`USER $APP_UID`) with a read-only
root filesystem compatible `securityContext` (the Helm chart's default).

## Image verification (cosign)

Every released image is signed **keyless** (GitHub Actions OIDC identity —
no long-lived signing keys) and the signature covers the immutable
**digest**, never a mutable tag, so `latest`/version tags can move without
invalidating the signature:

```bash
cosign verify ghcr.io/ryandanthony/nats-auth-callout-service@<digest> \
  --certificate-identity-regexp "^https://github.com/ryandanthony/nats-auth-callout-service/" \
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
```

Get the digest for a tag:

```bash
docker buildx imagetools inspect ghcr.io/ryandanthony/nats-auth-callout-service:v0.1.0 | grep Digest
```

### SBOM and provenance

The image manifest carries an SBOM and SLSA provenance attestation,
attached at build time (`docker/build-push-action` with `sbom: true`,
`provenance: true`) and re-attested under the same keyless identity, so
`cosign verify-attestation` has something to check without trusting the
registry's own attestation store:

```bash
cosign verify-attestation ghcr.io/ryandanthony/nats-auth-callout-service@<digest> \
  --type spdxjson \
  --certificate-identity-regexp "^https://github.com/ryandanthony/nats-auth-callout-service/" \
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
```

The full release workflow (build, sign, verify-before-publish) is in
[`.github/workflows/release.yml`](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/.github/workflows/release.yml)
— every release fails closed if signing doesn't succeed.
