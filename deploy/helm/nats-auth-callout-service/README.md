# nats-auth-callout-service

Helm chart for [`nats-auth-callout-service`](https://github.com/ryandanthony/nats-auth-callout-service):
an OIDC-token-gated NATS connect authorization callout. Deploys
`AuthCallout.Host` (doc 08) with any of the three routing config sources
(files, Kubernetes CRD, PostgreSQL) per `implementation-plan/17-helm-chart.md`.

## Installing

```bash
helm install my-callout deploy/helm/nats-auth-callout-service \
  --set secrets.issuerSeed.existingSecret=my-issuer-seed
```

The chart **never generates key material**. Every secret it needs is a
reference to a Kubernetes `Secret` you provision separately (see
[Secret provisioning](#secret-provisioning) below) — the chart only wires the
mounts and the bootstrap-file `SecretRef`s that point at them.

## Health probes (D21)

The container always exposes the pinned health port `8080` with three fixed
probe paths (not configurable — they're pinned in the application itself):

| Probe | Path | Meaning |
|-------|------|---------|
| Startup | `/startupz` | Gates slow first-config/secret resolution. |
| Liveness | `/healthz` | Process up, independent of NATS/config. |
| Readiness | `/readyz` | `200` only in the `Serving` state (config + NATS connected). |

## OpenTelemetry (D22)

`otel.serviceName` / `otel.otlpEndpoint` set `OTEL_SERVICE_NAME` /
`OTEL_EXPORTER_OTLP_ENDPOINT` on the container. Leave both empty to run
without an OTLP exporter (the SDK no-ops without an endpoint) — the app falls
back to its own default service name.

## Secret provisioning

For every distinct Kubernetes `Secret` referenced anywhere in `values.secrets`
or `values.source.postgres.connectionString`, the chart:

1. Adds a `secret`-typed Pod volume sourcing that Secret (`secretName: {name}`).
2. Mounts it read-only at `/var/run/secrets/{name}`.

This is the **k8sSecret mount contract** implemented by
`AuthCallout.Secrets.SecretResolver` (see
[`src/AuthCallout.Secrets/README.md`](../../../src/AuthCallout.Secrets/README.md)):
the app reads `/var/run/secrets/{name}/{key}` directly, no Kubernetes API
calls, no RBAC needed for secrets specifically.

Provision the secrets out-of-band, e.g.:

```bash
kubectl create secret generic my-issuer-seed --from-file=seed=./issuer.seed
kubectl create secret generic my-svc-seed --from-file=seed=./svc-user.seed
kubectl create secret generic my-assertion-a --from-file=seed=./assert-a.seed
```

Then reference them:

```yaml
secrets:
  issuerSeed:
    existingSecret: my-issuer-seed
    key: seed
  serviceUserSeed:
    enabled: true
    existingSecret: my-svc-seed
    key: seed
  assertionKeys:
    - kid: "2026-09-06a"
      existingSecret: my-assertion-a
      key: seed
      active: true
```

**Rotation** (ADR 0014): add the new key with `active: true`, flip the
previous entry to `active: false` (keep it in the list so in-flight
assertions signed with the old key still verify), roll the Deployment, then
drop the old entry once it has aged out. Never remove the currently-`active`
entry without first promoting a replacement.

## `source.kind` — the three config sources

Exactly one of `files`, `k8s`, `postgres` (ADR 0005: "sources are not
combined").

### 1. `files`

A directory of per-application routing files (doc 09 §Config files), mounted
from a ConfigMap.

```yaml
source:
  kind: files
  files:
    directory: /config/apps
    configMap:
      create: true
      files:
        notes.yaml: |
          schemaVersion: 1
          name: notes
          targetAccount: NOTES
          issuer:
            authority: https://id.example.com/realms/main
            jwksCache: 5m
            audience: [nats]
          profiles:
            - name: editor
              match: { realm_access.roles: [notes-editor] }
              grant: { publish: ["notes.{tenant}.>"], subscribe: ["notes.{tenant}.>"] }
              nameClaim: sub
              variables:
                - { name: tenant, claim: tenant_id, required: true, transforms: [lowercase, sanitize] }
```

To manage the per-app files yourself (e.g. a GitOps controller updates a
ConfigMap independently of this release), set
`source.files.configMap.create: false` and
`source.files.configMap.existingConfigMap: <name>`.

### 2. `k8s`

One `AuthApplication` Custom Resource per application, watched cluster-wide
and claimed by a label selector (ADR 0005). Installs the CRD
(`deploy/crd/authapplication-crd.yaml`) and the `ServiceAccount` +
`ClusterRole`/`ClusterRoleBinding` (`deploy/crd/rbac.yaml`) by default.

```yaml
source:
  kind: k8s
  k8s:
    labelSelector: "app.kubernetes.io/managed-by=nats-auth-callout"
    crds:
      install: true   # set false if another release/chart already installed the CRD
    rbac:
      create: true    # set false to bring your own ClusterRole/Binding
```

Apply `AuthApplication` CRs matching the selector separately, e.g.:

```yaml
apiVersion: authcallout.ants.zone/v1
kind: AuthApplication
metadata:
  name: notes
  labels: { app.kubernetes.io/managed-by: nats-auth-callout }
spec:
  targetAccount: NOTES
  issuer: { authority: https://id.example.com/realms/main, jwksCache: 5m }
  profiles: [...]
```

### 3. `postgres`

One row per application in `auth_applications`, polled on `pollInterval` with
`LISTEN`/`NOTIFY` as a latency optimization (doc 04 §Sources).

```yaml
source:
  kind: postgres
  postgres:
    connectionString:
      existingSecret: my-db-conn
      key: connectionString
    pollInterval: 5s
    notifyChannel: authcallout_config_changed
```

Provision the connection-string secret:

```bash
kubectl create secret generic my-db-conn \
  --from-literal=connectionString='Host=pg;Database=authcallout;Username=...;Password=...'
```

Run the migrations in `db/migrations/` against that database before pointing
the chart at it.

## Values reference

| Key | Default | Description |
|-----|---------|--------------|
| `image.repository` | `ghcr.io/ryandanthony/nats-auth-callout-service` | Container image. |
| `image.tag` | `""` (→ `.Chart.AppVersion`) | Image tag. |
| `image.pullPolicy` | `IfNotPresent` | |
| `replicaCount` | `2` | Instances behind the queue-group subscription (doc 08 HA guidance). |
| `serviceAccount.create` | `true` | |
| `podSecurityContext` / `securityContext` | non-root, read-only rootfs | doc 08 §Container. |
| `resources` | 100m/128Mi requests, 1/512Mi limits | |
| `health.port` | `8080` | Pinned (D21); do not change unless the app image also changes. |
| `service.enabled` / `.type` / `.port` | `true` / `ClusterIP` / `8080` | Health-port Service (no NATS traffic flows through it). |
| `otel.serviceName` | `""` | `OTEL_SERVICE_NAME` (D22). |
| `otel.otlpEndpoint` | `""` | `OTEL_EXPORTER_OTLP_ENDPOINT` (D22). |
| `natsUrl` | `nats://nats:4222` | Bootstrap `natsUrl`. |
| `bootstrap.jwks.*` | see `values.yaml` | Bootstrap `jwks` block. |
| `bootstrap.concurrency.*` | see `values.yaml` | Bootstrap `concurrency` block. |
| `bootstrap.anonymous.*` | see `values.yaml` | Bootstrap `anonymous` block. |
| `bootstrap.warnStaleness` / `.maxStaleness` / `.reconnectBackoff` | `60s` / `300s` / `5s` | ADR 0017 config-source watch health thresholds. |
| `secrets.issuerSeed.existingSecret` | `""` (**required**) | Issuer signing seed Secret name. |
| `secrets.serviceUserSeed.*` | disabled | Optional auth-service-user seed. |
| `secrets.xkey.*` | disabled | Optional xkey seed (ADR 0018 encrypted callout). |
| `secrets.assertionKeys` | `[]` | Assertion keyset (ADR 0011/0014). |
| `source.kind` | `files` | `files` \| `k8s` \| `postgres`. |
| `source.files.*` | see above | |
| `source.k8s.*` | see above | |
| `source.postgres.*` | see above | |
| `podDisruptionBudget.enabled` / `.minAvailable` | `true` / `1` | |
| `serviceMonitor.enabled` | `false` | Requires the Prometheus Operator CRD. |
| `extraEnv` / `extraVolumes` / `extraVolumeMounts` | `[]` | Escape hatches. |

## Validating

```bash
helm lint deploy/helm/nats-auth-callout-service --set secrets.issuerSeed.existingSecret=x
helm template deploy/helm/nats-auth-callout-service -f ci/values/files.yaml
helm template deploy/helm/nats-auth-callout-service -f ci/values/k8s.yaml
helm template deploy/helm/nats-auth-callout-service -f ci/values/postgres.yaml
```

`ci/values/{files,k8s,postgres}.yaml` are the golden fixtures for the three
source kinds (used by CI lint/template checks).
