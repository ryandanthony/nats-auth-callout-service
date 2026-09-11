---
sidebar_position: 1
---

# Health endpoints and probe semantics

The service is a long-lived worker, not a business HTTP API — its only
inbound HTTP surface is a minimal health/readiness API on a dedicated port
([D21](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/QUESTIONS.md): pinned to **`8080`**). Grounds:
[doc 08 §Health & readiness](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/design/08-technical-architecture-csharp.md#health--readiness),
[doc 05](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/design/05-runtime-and-lifecycle.md).

## The three endpoints

| Endpoint | Kubernetes probe | Tag | Semantics |
|----------|-------------------|-----|-----------|
| `/healthz` | liveness | `live` | `200` while the process is up and not deadlocked. Independent of config/NATS state — a transient `Idle` never restarts the pod. |
| `/readyz` | readiness | `ready` | `200` only in the `Serving` state: valid config active **and** NATS connected and subscribed **and** JWKS prewarmed. Any `Unhealthy` ready check returns `503`. |
| `/startupz` | startup | `startup` | `200` once the very first config decision has landed. Gates the k8s `startupProbe` so slow first-config/secret resolution doesn't trip liveness during boot. |

Each response is a JSON document (per-check status, description, duration,
non-secret `data`) — useful for humans and dashboards, but the health
endpoints are for orchestrator probes and quick inspection, not metrics
scraping (metrics flow via OTLP, see [Metrics](#metrics) below).

```bash
curl http://localhost:8080/healthz
curl http://localhost:8080/readyz
curl http://localhost:8080/startupz
```

## Component health checks

| Check | Tag(s) | Reports |
|-------|--------|---------|
| `self` | `live` | Process alive. |
| `nats-connection` | `ready` | The callout's own NATS connection is up and subscribed. `Unhealthy` while disconnected (auto-reconnects with capped backoff and resubscribes to the same queue group). |
| `config-active` | `ready` | A valid config batch is currently applied. |
| `config-source` | `ready` | The active source's watch/poll is healthy. Reports `lastSuccessfulSync` and staleness (see [Staleness and readiness](#staleness-and-readiness)). |
| `jwks` | informational (untagged) | Per-issuer JWKS freshness: `Healthy` when every issuer refreshed within its interval, `Degraded` on a stale-but-usable cache, `Unhealthy` only if an issuer has no usable keys at all. |
| `started` | `startup` | The JWKS cache manager completed its first prewarm of every configured issuer before the service entered `Serving`. |

## Staleness and readiness

A dead config watch (k8s `410 Gone`, a silently stopped file watcher) does
**not** stop the service — last-good config is still valid, and going dark
on a transient blip would be a worse outage. Staleness is instead a
bounded, first-class readiness signal ([ADR 0017](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0017-watch-loss-and-connection-loss-semantics.md)):

| Staleness (`now - lastSuccessfulSync`) | `config-source` health | Serving? | `/readyz` |
|----------------------------------------|------------------------|----------|-----------|
| < `warnStaleness` | `Healthy` (Fresh) | yes | `200` |
| ≥ `warnStaleness`, < `maxStaleness` | `Degraded` + alert | yes (Stale) | `200` |
| ≥ `maxStaleness` | `Unhealthy` | **yes, still last-good** | **`503`** |

At the hard bound the instance becomes **not-ready but keeps serving**
last-good config — the orchestrator stops routing to it / restarts it,
without the running instance ever denying a live connect on the basis of
stale-but-valid config. `warnStaleness` and `maxStaleness` are per-source
[bootstrap fields](../configuration/bootstrap-reference#source).

A **lost NATS connection**, by contrast, fails closed immediately: the
service transitions `Serving → Idle`, holds no subscription, and answers
nothing until it reconnects.

## Metrics

Exported via OpenTelemetry OTLP, configurable at deployment time via
environment variables ([D22](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/QUESTIONS.md)):

| Env var | Purpose |
|---------|---------|
| `OTEL_SERVICE_NAME` | Service name reported to your OTel backend. Falls back to `nats-auth-callout-service` if unset. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP collector endpoint. The SDK no-ops (exports nothing) if unset. |

At minimum: total requests, grants by outcome, denials by reason,
validation failures, config reloads (applied/rejected), events
published/dropped, token-store writes/failures, JWKS fetches/cache hits,
current state (idle/serving), plus the staleness metrics
`config_last_sync_timestamp`, `config_staleness_seconds`,
`watch_reconnects_total`, `watch_relists_total`, `watch_failures_total`.

**Traces**: one span per auth request covering the four resolution stages
(issuer → validate → app/profile → mint), so latency and failure
attribution are visible per stage. The [Aspire dev loop](../getting-started/running-the-service#local-dev-loop-aspire)
gives you this dashboard for free while iterating locally.

## Auth outcome events (application-level, not operator metrics)

Separately from metrics/traces, each application can configure `events.
success`/`events.failure` subjects ([ADR 0008](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0008-publish-auth-outcome-events.md))
to get a real-time, per-application NATS feed of auth decisions —
best-effort, never on the critical path, secret-free. See the
[event schema reference](./event-schema) for the payload shape and reason
codes.

## HA (multiple instances)

Run **≥ 2 replicas** behind the same NATS queue group (`auth-callout`) so
`$SYS.REQ.USER.AUTH` load-balances across instances and one instance's
restart doesn't drop auth capacity. Every instance in the group needs the
**same** shared secrets (issuer seed, service user seed, assertion keyset,
optional xkey seed) — they are one logical signer, not per-instance
identities. The [Helm chart](./deployment) defaults `replicaCount: 2` and
ships a `PodDisruptionBudget` (`minAvailable: 1`) for exactly this.
