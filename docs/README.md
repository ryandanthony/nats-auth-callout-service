# nats-auth-callout-service — design notes

Design notes for `~/code/nats-auth-callout-service`, collected ahead of a
planned rewrite. Goal: capture how the service works today, then gather
better input before rebuilding it.

Source repo: `~/code/nats-auth-callout-service`
(GitHub: `ryandanthony/nats-auth-callout-service`, images at
`ghcr.io/ryandanthony/nats-auth-callout-service`).

## Notes in this folder

- [architecture-decision-log/](./architecture-decision-log/README.md) — the
  distilled decisions (ADRs 0001-0008) driving the rewrite.
- [design/](./design/README.md) — cohesive, language-agnostic design unifying
  the ADRs, with diagrams.
- [current-state.md](./current-state.md) — what the service does today
  (architecture, config, permission model, testing), reverse-engineered from
  the code on 2026-09-05.
- [rewrite-input.md](./rewrite-input.md) — scratchpad for gathering
  requirements, pain points, and open questions for the rewrite.
