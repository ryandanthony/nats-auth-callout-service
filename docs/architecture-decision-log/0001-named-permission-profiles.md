# 0001 — Named permission profiles per application

- **Status:** proposed
- **Date:** 2026-09-05
- **Deciders:** Ryan

## Context

An application often serves several kinds of principal at once: multiple microservices and one or more classes of human user, each needing different, scoped NATS subjects within the same application and NATS account. The authorization model must express that directly.

## Decision

Each application owns an ordered list of **named permission profiles**. A connection's permissions come from the **first** profile whose `Match` predicate is satisfied by the connection's validated token claims (order is the tie-breaker; multiple matches are not an error). If a validated token matches **no** profile, the connection is **denied with a clear log** rather than falling back to anonymous: a valid token that fits no profile is either a misconfiguration or an unauthorized principal.

A profile contains:

- `Name` — identifier for logging/metrics.
- `Match` — a claim-based predicate (see claim matching below).
- `Publish` / `Subscribe` — static subject grants.
- `ScopeClaim` + `PublishPerScope` / `SubscribePerScope` — per-value templated grants; `{scope}` expands once per value of `ScopeClaim` (so a token carrying multiple values gets subjects for each). **Generalized by [ADR 0009](./0009-templated-subject-variables.md)** into named variables bound to transformed claims; `{scope}` is one such variable.
- `NameClaim` — the claim used as the minted user JWT name.
- Expiry settings — the minted JWT prefers the token's own `exp`, falling back to a configured lifetime.

Claim matching is shared with application selection (ADR 0003): one matcher supporting nested claim paths (e.g. `realm_access.roles`), array-contains, AND across keys, and OR within a key's value list.

A connection presenting **no** token receives the anonymous grant (ADR 0004) rather than a profile. A connection presenting an invalid token is denied, not downgraded to anonymous (full decision tree in ADR 0003).

## Consequences

- One application/account serves many services and user classes with distinct, least-privilege permissions.
- Adding a new principal class is a config change (a new profile), not a code change.
- Config validation flags profiles that can never match and ambiguous overlaps.
- Profile selection is fully deterministic: first match wins, no match denies.

## Related

- Design: [02 — Authorization model](../design/02-authorization-model.md), [03 — Resolution pipeline](../design/03-resolution-pipeline.md).
- ADRs: [0002](./0002-service-auth-via-oidc-not-known-keys.md) (services are profiles matched on a service claim), [0003](./0003-resolution-order-issuer-validate-app-profile.md) (profile is the final selection stage), [0007](./0007-internal-configuration-model.md) (profile fields), [0009](./0009-templated-subject-variables.md) (generalizes `{scope}` into transformed variables).
