---
sidebar_position: 2
---

# Bootstrap file reference

Schema: [`schemas/config/bootstrap/v1.json`](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/schemas/config/bootstrap/v1.json)
(`$id: .../schemas/config/bootstrap/v1.json`). Loaded once at startup, never
hot-reloaded ([doc 04](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/design/04-configuration.md)).
Strict: unknown top-level fields are rejected.

## Full example

```yaml
schemaVersion: 1
natsUrl: nats://nats:4222
issuerSeed:        { kind: file, path: /secrets/issuer.seed }
serviceUserSeed:   { kind: k8sSecret, name: callout-svc, key: seed }
xkeySeed:          { kind: file, path: /secrets/xkey.seed }   # optional, ADR 0018
assertionKeys:
  - kid: 2026-09-06a
    key: { kind: file, path: /secrets/assert-a.seed }
    active: true
  - kid: 2026-08-01a
    key: { kind: file, path: /secrets/assert-old.seed }
    active: false
source:
  kind: files                        # or k8s, or postgres
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
  queueCapacity: 256
anonymous:
  targetAccount: ANON
  expiry: 5m
  events: { success: anon.auth.success, failure: anon.auth.failure }
```

## Top-level fields

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `schemaVersion` | integer | yes | Must be `1`. |
| `natsUrl` | string | yes | The NATS server URL the service connects to. |
| `issuerSeed` | [SecretRef](#secretref) | yes | Signs minted user JWTs and the callout response ([05](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/design/05-runtime-and-lifecycle.md)). |
| `serviceUserSeed` | [SecretRef](#secretref) | no | The callout's own NATS connection credential. |
| `xkeySeed` | [SecretRef](#secretref) | no | Optional Curve/xkey seed for encrypted `$SYS.REQ.USER.AUTH` traffic ([ADR 0018](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0018-xkey-encrypted-callout.md)). Unset = unencrypted (default). |
| `assertionKeys` | array of [AssertionKey](#assertionkey) | no | The assertion signing keyset ([ADR 0011](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0011-signed-identity-assertion.md), [ADR 0014](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0014-key-rotation-orchestration.md)). Exactly one entry must be `active`. |
| `source` | [Source](#source) | yes | Which routing provider is active plus its settings. |
| `jwks` | [Jwks](#jwks) | yes | JWKS cache manager settings. |
| `concurrency` | [Concurrency](#concurrency) | yes | In-process parallelism/backpressure. |
| `anonymous` | [Anonymous](#anonymous) | yes | Global anonymous grant settings. |

## `SecretRef`

| Field | Type | Notes |
|-------|------|-------|
| `kind` | enum | One of `inline`, `file`, `k8sSecret`, `vault`. |
| `value` | string | Used when `kind: inline`. |
| `path` | string | Used when `kind: file`. |
| `name` | string | Used when `kind: k8sSecret` (Secret name). |
| `key` | string | Used when `kind: k8sSecret` (key within the Secret). |

## `AssertionKey`

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `kid` | string | yes | Key id stamped in each assertion header. Cannot be the literal `current`. |
| `key` | [SecretRef](#secretref) | yes | The signing/verification key material. |
| `active` | boolean | yes | Exactly one entry across the list must be `true` — that key signs new assertions; the rest are verify-only. |

## `Source`

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `kind` | enum | yes | `files`, `k8s`, or `postgres`. See [Config sources](./sources/overview). |
| `directory` | string | when `kind: files` | Directory of per-application files. |
| `labelSelector` | string | when `kind: k8s` | Owner label selector claiming this instance's CRs. |
| `connectionString` | [SecretRef](#secretref) | when `kind: postgres` | Database connection string. |
| `pollInterval` | duration string | no | PostgreSQL polling cadence (default `30s`); LISTEN/NOTIFY is a latency-only optimization on top. |
| `notifyChannel` | string | no | PostgreSQL `NOTIFY` channel name (default `auth_applications_changed`). |
| `warnStaleness` | duration string | yes | Config-source `Degraded` + alert threshold ([ADR 0017](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0017-watch-loss-and-connection-loss-semantics.md)). |
| `maxStaleness` | duration string | yes | Config-source `Unhealthy` threshold → `/readyz` 503, still serving last-good. |
| `reconnectBackoff` | duration string | yes | Capped relist/rewatch backoff. |

## `Jwks`

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `refreshInterval` | duration string | yes | Proactive re-fetch cadence per issuer. |
| `fetchTimeout` | duration string | yes | Hard timeout on any single discovery/JWKS fetch (fail closed). |
| `prewarmRequired` | boolean | yes | All issuers must warm before the service enters `Serving`. |

## `Concurrency`

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `maxDegreeOfParallelism` | integer | yes | Max auth requests processed in parallel per instance. |
| `perRequestTimeout` | duration string | yes | Per-request deadline; keep below the NATS server's callout timeout. |
| `queueCapacity` | integer | no | Optional accept-queue depth before shedding. |

## `Anonymous`

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `targetAccount` | string | yes | NATS account anonymous connections are minted into. |
| `expiry` | duration string | yes | Sole expiry source for anonymous grants (they carry no token `exp`). |
| `events` | [EventSubjects](#eventsubjects) | no | `success`/`failure` subjects for anonymous auth outcomes. |

## `EventSubjects`

| Field | Type | Notes |
|-------|------|-------|
| `success` | string | Subject for successful outcomes. |
| `failure` | string | Subject for failed outcomes. |
