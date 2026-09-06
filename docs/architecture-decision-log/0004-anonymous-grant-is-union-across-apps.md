# 0004 — Anonymous grant is the union across applications

- **Status:** proposed
- **Date:** 2026-09-05
- **Deciders:** Ryan

## Context

An anonymous connection carries no token, so it cannot be routed to any single application by issuer or claim. It still needs a defined, useful set of permissions (for example bootstrap and `_INBOX.>` subjects), and that set should track the applications actually configured.

## Decision

The anonymous grant is the **union of every configured application's anonymous contribution**.

- Each application declares an `AnonymousProfile`: the publish/subscribe subjects it contributes to anonymous connections. It is optional — an application that omits it contributes nothing to the union (opt-out).
- An unauthenticated connection receives the union of all applications' `AnonymousProfile` subjects. There is no default-application dependency.
- Adding an application adds its anonymous subjects; removing one removes them.
- A minted user JWT carries a single account, but the union spans applications that may target different accounts, so anonymous connections are minted into a dedicated, globally configured anonymous account (e.g. `ANON`) with a global expiry.
- Identical subjects contributed by multiple applications are de-duplicated.

## Consequences

- Anonymous permissions are compositional and stay in sync with the configured set of applications, consistent with add/remove-an-app config semantics (ADR 0005).
- Requires a global anonymous account and expiry setting.

## Related

- Design: [02 — Authorization model](../design/02-authorization-model.md#anonymous-grant-is-a-union-adr-0004), [04 — Configuration](../design/04-configuration.md).
- ADRs: [0003](./0003-resolution-order-issuer-validate-app-profile.md) (no-token branch grants this union), [0005](./0005-config-abstraction-and-sources.md) (add/remove-an-app semantics), [0007](./0007-internal-configuration-model.md) (`anonymousProfile` field).
