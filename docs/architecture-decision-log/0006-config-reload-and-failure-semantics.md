# 0006 — Config reload and failure semantics

- **Status:** proposed
- **Date:** 2026-09-05
- **Deciders:** Ryan

## Context

The routing/apps config layer (ADR 0005) auto-refreshes for every source. The service must never operate on a partial or inconsistent configuration, and its behavior on reload, invalid input, and cold start must be fully defined.

## Decision

### Reload

On refresh, new connections use the new config. Already-minted NATS user JWTs keep their permissions until their TTL expires, since a minted JWT cannot be revoked. This is acceptable.

### Steady-state failure

A refresh that is invalid, unparseable, or conflicting (for example a duplicate application name, or two applications colliding on the same issuer and `Match`) is rejected as a whole batch. The service keeps running on the last-good config and emits a loud error log and a metric. A single offending unit blocks the entire batch; a partial config is never applied.

### Cold start

Until a valid, conflict-free config is loaded, the service is **idle with no config**: it does not exit, does not apply any partial or conflicting config, and does not connect or subscribe to NATS at all. It connects and subscribes to `$SYS.REQ.USER.AUTH` only once a valid config is present, and it becomes idle again if the config is ever lost.

## Consequences

- The service only ever runs on a fully valid, conflict-free config, or nothing.
- No window of partial or misconfigured authorization exists during reloads or startup.
- While idle the service holds no NATS connection, so NATS's own rules govern (typically deny) and no callout responses are produced.
- Requires last-good caching, batch-level validation including cross-unit conflict detection, and an idle/ready state machine gating the NATS connection.

## Related

- Design: [05 — Runtime and lifecycle](../design/05-runtime-and-lifecycle.md), [04 — Configuration](../design/04-configuration.md).
- ADRs: [0005](./0005-config-abstraction-and-sources.md) (sources that reload), [0007](./0007-internal-configuration-model.md) (validated model).
