# 0017 — Watch-loss and connection-loss semantics

- **Status:** proposed
- **Date:** 2026-09-06
- **Deciders:** Ryan

## Context

[ADR 0006](./0006-config-reload-and-failure-semantics.md) defines what happens when config is **bad** (reject the batch, keep last-good) and [05 — Runtime and lifecycle](../design/05-runtime-and-lifecycle.md) defines the `Serving`/`Idle` states. Neither covers two runtime failures where nothing is *wrong* with the data but the service can no longer trust that it is current or reachable:

- **Lost NATS connection.** The callout's own connection to NATS drops. It is no longer subscribed to `$SYS.REQ.USER.AUTH`, so it answers nothing.
- **Dead config watch.** The k8s CRD watch (or file watcher) silently stops delivering events while the process and the NATS connection stay healthy. The callout keeps serving on a **stale** last-good config, unaware that an application was added, removed, or had its permissions tightened.

These need distinct policies. A lost connection is inherently fail-closed. A dead watch is not unsafe (last-good is still valid config), but the drift risk grows with time, and reacting too aggressively (denying all auth on staleness) would convert a "slightly behind" condition into a total outage on the connect path.

## Decision

Distinguish **"I can't hear updates"** (stale) from **"I have bad data"** (invalid, [ADR 0006](./0006-config-reload-and-failure-semantics.md)). Fail closed on unreachability, shed load via readiness on staleness, and always attempt recovery.

### 1. Lost NATS connection — fail closed, auto-recover

- On connection loss the service transitions `Serving → Idle`: it holds no subscription and answers nothing. The NATS server times out those callouts and clients retry.
- The client **auto-reconnects with capped backoff** and, on reconnect, **resubscribes with the same queue group** (`auth-callout`, [multiple instances](../design/08-technical-architecture-csharp.md#multiple-instances-ha-and-exactly-once-handling)).
- The `nats-connection` readiness check ([health](../design/08-technical-architecture-csharp.md#health--readiness)) is `Unhealthy` while disconnected, so `/readyz` returns 503; it returns to `Serving`/200 on reconnect. No new policy needed beyond reflecting connection state in the state machine and health.

### 2. Dead config watch — keep last-good, bound the staleness

Config-watch loss does **not** stop the service answering: last-good is still valid, and going dark on a transient API-server blip would be a worse outage. Instead, staleness becomes a first-class, time-bounded health signal.

- **Detect.** Track `lastSuccessfulSync` per source, updated on every successful list/relist, **not** only on changes, so "no changes" and "not syncing" are distinguishable. A k8s `410 Gone` or closed watch stream triggers relist-and-rewatch; repeated relist failure marks the watch unhealthy. The file watcher is analogous (watcher handle dead / directory unreadable).
- **Two thresholds** drive an orthogonal config-freshness substate of `Serving`:

| Staleness (`now - lastSuccessfulSync`) | `config-source` health | Serving? | `/readyz` |
|----------------------------------------|------------------------|----------|-----------|
| < `warnStaleness` | `Healthy` | yes (Fresh) | 200 |
| ≥ warn, < `maxStaleness` | `Degraded` + alert | yes (Stale) | 200 |
| ≥ `maxStaleness` | `Unhealthy` | **yes, still last-good** | **503** |

- **At the hard bound, become not-ready but keep serving last-good.** Flipping `/readyz` to 503 lets the orchestrator stop routing to / restart this instance (another replica or a fresh start takes over and relists from scratch), **without** the running instance suddenly denying every connect. This is the proportionate lever: stale is not wrong, so shed via readiness, don't fail closed on valid data.
- **Always recover.** Relist/rewatch runs continuously with capped exponential backoff; a successful sync clears staleness and returns to `Healthy`/Fresh.

### Why staleness does not fail closed

Stale ≠ invalid. The last-good config is valid and was authored deliberately; it is only possibly out of date. Denying all auth on staleness turns a "might be slightly behind" state into a total connect-path outage, the opposite of the goal for infrastructure on the critical path. Readiness-based shedding plus alerting is proportionate and self-healing via restart; an operator (or the orchestrator) decides, and recovery is automatic once the watch resyncs.

## Consequences

- Two clear, different postures: connection loss = don't answer (fail closed, inherent); watch loss = keep answering last-good, alert, and become not-ready past a bound.
- `Serving` gains an orthogonal freshness substate (`Fresh → Stale(Degraded) → Stale(Unready)`) driven purely by `lastSuccessfulSync` age; it does not change the authorization logic.
- The hard staleness bound is a readiness signal, not a kill switch, so a stale instance never causes a self-inflicted auth outage; the orchestrator resolves it.
- New per-source config (`warnStaleness`, `maxStaleness`, `reconnectBackoff` — duration strings) and new observability (`config_last_sync_timestamp`, `config_staleness_seconds`, `watch_reconnects_total`, `watch_relists_total`, `watch_failures_total`).
- The `config-source` health check now reports `lastSuccessfulSync` and current staleness in its `data`, so operators can see drift directly.
- Requires each source provider to expose a liveness/`lastSuccessfulSync` signal, not just a change callback ([ADR 0005](./0005-config-abstraction-and-sources.md) provider contract gains a health surface).

## Related

- Design: [05 — Runtime and lifecycle](../design/05-runtime-and-lifecycle.md) (state machine, failure posture), [08 — Technical architecture (C#)](../design/08-technical-architecture-csharp.md#watch-loss-and-connection-loss) (implementation), [04 — Configuration](../design/04-configuration.md) (source settings).
- ADRs: [0005](./0005-config-abstraction-and-sources.md) (sources and watch), [0006](./0006-config-reload-and-failure-semantics.md) (bad-config semantics this complements).
