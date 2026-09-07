# P13 — Host, health, lifecycle (integration point)

**Wave:** 2 (last component packet; the coordinator may take this one)
**Depends on:** P03, P05, P06, P07, P08, P09, P10 (files source minimum)
**Blocks:** P14, P15, P17, M2
**Estimated size:** 1.5–2 agent-days

## Scope

`AuthCallout.Host`: DI composition, the service state machine, config-apply
loop (keep-last-good), NATS connection management, health endpoints, OTel.
This is where every seam meets — the highest-conflict-risk packet, so it
starts only after its dependencies' interfaces are exercised.

Grounding: `docs/design/05-runtime-and-lifecycle.md` (state machine, reload
rules, observability), doc 08 (§Process composition, §Health & readiness,
§Config reload swap, §Watch-loss/connection-loss), ADR 0006, ADR 0017,
D21 (port 8080, endpoint names pinned), D22 (OTel configurable via env).

## Files owned

```
src/AuthCallout.Host/**
tests/AuthCallout.Nats.Tests/Host/** (or tests/AuthCallout.Host.Tests/** — create it via coordinator)
```

## Tasks

1. **Composition** (`Program.cs`): Generic Host + minimal `WebApplication`
   for health on **port 8080** (D21). Bind + validate bootstrap config
   (`IValidateOptions`, fail-closed), resolve secrets (P03), select exactly
   one routing provider (files/k8s/postgres) from `source.kind`, register
   everything per doc 08's process-composition diagram.
2. **State machine** (`IServiceStateProvider`, doc 05):
   Starting → Idle/Serving → Stopping exactly per the state diagram:
   - **Idle holds no NATS connection at all** (doc 05).
   - Serving entry requires: valid config + secrets resolved + JWKS prewarm
     complete (P06, `prewarmRequired`) + NATS connected + subscribed.
   - Freshness substates Fresh/Stale(Degraded)/Stale(Unready) from provider
     `LastSuccessfulSync` vs `warnStaleness`/`maxStaleness` (ADR 0017 table).
3. **Config-apply loop** (ADR 0006): consume provider batches; valid →
   build immutable `ActiveConfig` snapshot, atomic reference swap (new
   connections only); invalid → keep last-good, log + metric; no config ever
   → Idle. On apply: trigger P08 bucket provisioning + discovery/keys
   publication, notify P06 issuer tracking.
4. **`NatsConnectionManager`**: connect with service user seed, reconnect
   with capped backoff, resubscribe on reconnect (queue group preserved);
   disconnect → Serving→Idle transition per doc 08 watch-loss section; owns
   starting/stopping the P07 responder loop.
5. **Health endpoints** (doc 08 table, D21): `/healthz` (live: `self`),
   `/readyz` (ready: `nats-connection`, `config-active`, `config-source`),
   `/startupz` (startup: `started`), `jwks` informational; JSON response
   writer with per-check detail, no secrets. Staleness → Degraded at warn,
   Unhealthy (503) at max while still serving last-good (ADR 0017).
6. **OTel** (doc 05 observability, D22): resource/service name from
   `OTEL_SERVICE_NAME` (default `nats-auth-callout-service`), OTLP endpoint
   from `OTEL_EXPORTER_OTLP_ENDPOINT`; one span per auth request with the
   four stage events; metrics from doc 05/08 lists (auth outcomes by reason,
   duration, in-flight, config_last_sync_timestamp, config_staleness_seconds,
   watch_reconnects_total, JWKS metrics, events_publish_failures_total,
   orphaned_buckets). Wire the meters the component packets registered.
7. **Graceful shutdown**: SIGTERM → stop pulling requests, drain in-flight
   (`Parallel.ForEachAsync` awaits), close NATS, exit (doc 05 Stopping).

## ADR→test map

Host-level tests via **Alba** (health endpoints) + component substitutes:

| Behavior (source) | Test |
|-------------------|------|
| Every doc 05 state transition incl. bad-reload keep-last-good | state machine unit tests |
| Idle = no NATS connection (doc 05) | substitute connection factory never called |
| Serving gated on prewarm + config + connection (doc 08) | ordering test |
| Snapshot swap: in-flight keeps old, next gets new (doc 08) | test |
| /healthz vs /readyz vs /startupz tag filtering, JSON detail, 503 rules (doc 08, D21) | Alba |
| ADR 0017 staleness table: all three rows | clock-driven tests |
| Liveness unaffected by Idle (doc 08) | Alba |
| Env-driven OTel config (D22) | config test |
| Drain on shutdown | test |

## Definition of done

- [ ] All tests green; service boots against a files-source fixture dir and
      reaches Serving with a local `nats-server` (manual smoke, documented).
- [ ] Health JSON never contains secret material (spy test).
- [ ] Coordinator announces M1 when this + all Wave 1–2 packets are green.
