# 0008 — Publish auth outcome events per application

- **Status:** proposed
- **Date:** 2026-09-05
- **Deciders:** Ryan

## Context

Operators and applications need visibility into authentication and authorization outcomes: which principals connected, which profile they matched, and which attempts were denied or failed. Metrics and logs cover aggregate and operator views, but applications also benefit from a real-time, subject-based feed they can subscribe to (for audit trails, session tracking, or reacting to new connections). NATS itself is the natural transport.

## Decision

On every resolved auth request, the callout publishes an **auth outcome event** to a NATS subject configured per application, for both success and failure.

- Each application configures its event subjects (see ADR 0007): explicit `Success` and `Failure` subjects (no templated form). A per-application setting keeps each application's audit stream isolated to its own account/subject space.
- The event is published for:
  - **success** — a token validated and a profile matched, and
  - **failure** — validation failed, no application or profile matched, the scope requirement was unmet, or the request errored.
- Anonymous outcomes are treated as their own case: because anonymous is the cross-application union (ADR 0004) and not tied to one application, anonymous events use globally configured subjects in the bootstrap layer. There is **no default subject**, so anonymous events are **off** unless a subject is set.
- The event payload is a **versioned** JSON document carrying a `schemaVersion` field, and includes at least: timestamp, outcome (`success`/`failure`), reason code, application name, matched profile name (on success), principal identifier (e.g. `sub`/`azp`, never the raw token), issuer, and the connecting user NKey. Payloads carry no secrets or raw tokens.
- Publishing is **best-effort and non-blocking**: it must never delay or block the auth response, and a publish failure is logged/metered but does not fail the auth decision.
- Event publishing is optional per application; when no subject is configured, no event is published for that application.

## Consequences

- Applications get a real-time, per-application feed of auth successes and failures over NATS, isolated by account/subject.
- The callout's own NATS user must be permitted to publish to the configured subjects.
- Adds an outcome event schema that must stay stable and secret-free; the payload's `schemaVersion` allows it to evolve without breaking consumers.
- Best-effort delivery means events can be lost under broker/backpressure failure; consumers must not treat the stream as guaranteed-complete audit.

## Related

- Design: [03 — Resolution pipeline](../design/03-resolution-pipeline.md#outcome-events-adr-0008), [05 — Runtime and lifecycle](../design/05-runtime-and-lifecycle.md#auth-outcome-events-adr-0008).
- ADRs: [0003](./0003-resolution-order-issuer-validate-app-profile.md) (outcomes come from the decision tree), [0004](./0004-anonymous-grant-is-union-across-apps.md) (anonymous events), [0007](./0007-internal-configuration-model.md) (`EventSubjects` field).
