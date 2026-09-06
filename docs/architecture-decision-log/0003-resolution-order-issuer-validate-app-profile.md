# 0003 — Resolution order: issuer, validate, app Match, profile Match

- **Status:** proposed
- **Date:** 2026-09-05
- **Deciders:** Ryan

## Context

One OIDC issuer (e.g. one Keycloak realm) may back several applications, each with its own permission model and NATS account. Selecting the application must therefore work even when many applications share an issuer, and it must be safe: no token claim can be trusted until the token's signature is verified, and the `iss` claim is what selects the keys used to verify it.

## Decision

Every connection resolves through a strict, fixed pipeline:

1. **Issuer (pre-validation).** Read `iss` unvalidated. It selects both the candidate applications configured for that authority and the JWKS/authority used to validate the token. Issuer is always the top-level check.
2. **Validate.** Verify the token against that issuer's JWKS, once, shared by all candidate applications on that authority. Only after this are claims trusted.
3. **Application `Match`.** Among the issuer's candidate applications, select the one whose claim-based `Match` is satisfied (e.g. `audience`). A single candidate with a unique issuer skips this step.
4. **Profile `Match`.** Within the chosen application, select the permission profile (ADR 0001).

Application selection by claim is always a second stage scoped to one issuer's candidates. Routing is independent of audience *validation*: `ValidateAudience` is a separate setting, and the `audience` claim may be used purely as a routing selector. The same matcher implementation serves steps 3 and 4.

### Decision tree

The authoritative outcome for every connection:

1. **No token presented** — mint the anonymous grant (the cross-application union, ADR 0004).
2. **A token is presented but its `iss` matches no configured application** — deny with a clear log. Without a configured authority there is no JWKS to validate against, so the token is untrusted and never anonymous.
3. **A token is presented but invalid** — expired, bad signature, or otherwise failing validation against its issuer's JWKS — **deny with a clear log**. A present-but-bad token is never downgraded to anonymous.
4. **Valid token, issuer matches, but the app-level `Match` selects zero apps** — deny with a clear log.
5. **Valid token, but more than one candidate application's `Match` is satisfied** — deny with a clear log (no union, no tie-breaker).
6. **Valid token, exactly one application selected, but no profile `Match` is satisfied** — deny with a clear log (ADR 0001).
7. **Valid token, one application and one profile selected** — mint the profile's grant (first matching profile wins, ADR 0001).

Anonymous (case 1) is the only non-token path; every presented token must fully validate and land on a profile to be granted anything.

## Consequences

- Multiple applications share one issuer, disambiguated by any validated claim, not only audience.
- Claim-based routing is safe because it runs after signature validation.
- A token that satisfies more than one candidate application's `Match` is **denied with a clear log**. It cannot map to a single account by union (a user JWT has one account), and there is no tie-breaker. Configuration should avoid overlapping app `Match` predicates within one issuer.
- Configuration is rejected when two applications are indistinguishable (same issuer and no distinguishing `Match`).

## Related

- Design: [03 — Resolution pipeline](../design/03-resolution-pipeline.md), [02 — Authorization model](../design/02-authorization-model.md#decision-tree-adr-0003).
- ADRs: [0001](./0001-named-permission-profiles.md) (profile stage), [0002](./0002-service-auth-via-oidc-not-known-keys.md) (principals), [0004](./0004-anonymous-grant-is-union-across-apps.md) (no-token branch), [0008](./0008-publish-auth-outcome-events.md) (outcome events).
