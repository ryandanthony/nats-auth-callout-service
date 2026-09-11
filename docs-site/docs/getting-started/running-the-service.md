---
sidebar_position: 3
---

# Running the service

## Docker

Signed, multi-arch images are published to GHCR on every tagged release
(see [Deployment → Image verification](../operations/deployment#image-verification-cosign)):

```bash
docker pull ghcr.io/ryandanthony/nats-auth-callout-service:latest
```

Run it with a bootstrap file and (for the files source) a directory of
per-application config mounted in:

```bash
docker run --rm \
  -p 8080:8080 \
  -v $PWD/bootstrap.yaml:/config/bootstrap.yaml:ro \
  -v $PWD/apps:/config/apps:ro \
  -e OTEL_SERVICE_NAME=nats-auth-callout-service \
  ghcr.io/ryandanthony/nats-auth-callout-service:latest
```

Health endpoints come up on port `8080`: `/healthz`, `/readyz`,
`/startupz` (see [Operations → Health endpoints](../operations/health-and-probes)).

## Bootstrap file

Every deployment needs exactly one bootstrap file (schema
`schemas/config/bootstrap/v1.json`), loaded once at startup and never
hot-reloaded. Minimal files-source example:

```yaml
schemaVersion: 1
natsUrl: nats://nats:4222
issuerSeed:        { kind: file, path: /secrets/issuer.seed }
serviceUserSeed:   { kind: file, path: /secrets/service-user.seed }
assertionKeys:
  - kid: 2026-09-06a
    key: { kind: file, path: /secrets/assert-a.seed }
    active: true
source:
  kind: files
  directory: /config/apps
  warnStaleness: 60s
  maxStaleness: 15m
  reconnectBackoff: 5s
jwks:
  refreshInterval: 5m
  fetchTimeout: 5s
  prewarmRequired: true
concurrency:
  maxDegreeOfParallelism: 64
  perRequestTimeout: 1500ms
anonymous:
  targetAccount: ANON
  expiry: 5m
```

See the [configuration reference](../configuration/overview) for every
field, and [Config sources](../configuration/sources/overview) for the k8s
and PostgreSQL alternatives to `source.kind: files`.

## Kubernetes / Helm

```bash
helm install auth-callout deploy/helm/nats-auth-callout-service \
  --set source.kind=files
```

See [Deployment](../operations/deployment) for the full Helm values and the
three `source.kind` install patterns.

## Verifying it's up

```bash
curl http://localhost:8080/healthz   # 200 once the process is alive
curl http://localhost:8080/readyz    # 200 once config + NATS + JWKS prewarm are all healthy
curl http://localhost:8080/startupz  # 200 once the first config decision has landed
```

`/readyz` returns `503` with `"state":"Starting"` for the first 15-30
seconds in a typical dev loop while JWKS prewarms and the config source
applies its first batch — this is normal, not a failure (verified live in
[`dev/README.md`](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/dev/README.md)).

## Local dev loop (Aspire)

For iterating on the service itself (not just consuming it), two
[.NET Aspire](https://aspire.dev) AppHosts under [`dev/`](https://github.com/ryandanthony/nats-auth-callout-service/tree/main/dev)
spin up NATS + Keycloak + the callout + two sample services with one
command:

```bash
dotnet run --project dev/AppHost.Local     # files source, inner loop
dotnet run --project dev/AppHost.K8s       # CRD source, local k3s
```

See [`dev/README.md`](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/dev/README.md)
for prerequisites (a trusted local HTTPS dev cert is required) and what
"working" looks like.
