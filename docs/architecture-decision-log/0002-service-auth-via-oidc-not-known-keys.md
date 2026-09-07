# 0002 — Service-to-service auth via OIDC, not known keys

- **Status:** proposed
- **Date:** 2026-09-05
- **Deciders:** Ryan

## Context

Microservices need to connect to NATS and receive scoped permissions. They should authenticate the same way users do, through the identity provider, so there are no pre-shared NATS keys to distribute or rotate and no all-or-nothing grants.

## Decision

A microservice authenticates to the callout with an **OIDC client-credentials JWT** (each service is its own OIDC client). Its token flows through the same validation and resolution pipeline as a user token (ADR 0003), and its permissions come from a named profile (ADR 0001) matched on a service-identifying claim. Because profile `Match` is generic over any claim path (ADR 0007), no service-identifying claim is hard-coded; **`azp`** is the documented default (Keycloak sets it to the calling client's id under client-credentials), and an operator may match on `client_id`, `aud`, or a custom claim instead.

- Services receive least-privilege, scoped subjects from their profile.
- No NATS keys are provisioned for service auth; trust is delegated entirely to the OIDC provider. There is no NKey allow-list or break-glass bypass: every connection authenticates through token validation and is authorized by a profile (or the anonymous grant when no token is presented; a presented-but-invalid token is denied, never downgraded to anonymous, ADR 0003).

## Consequences

- Services and users share one authentication and authorization path.
- Each service requires an OIDC client in the relevant realm; the callout keys a profile off the service-identifying claim (default `azp`, configurable).
- Key distribution and rotation for services disappears; provider availability and token validation become the trust anchor.

## Related

- Design: [02 — Authorization model](../design/02-authorization-model.md#principals), [03 — Resolution pipeline](../design/03-resolution-pipeline.md).
- ADRs: [0001](./0001-named-permission-profiles.md) (profiles carry the grant), [0003](./0003-resolution-order-issuer-validate-app-profile.md) (same validation pipeline as users).
