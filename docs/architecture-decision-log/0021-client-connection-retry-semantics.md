# 0021 — Client connection retry and auth-violation semantics

- **Status:** proposed
- **Date:** 2026-11-15
- **Deciders:** Ryan

## Context

NACS fails closed (ADR 0003): a presented-but-invalid token is **denied**, never
downgraded to anonymous. That changes what a correct client connect loop looks
like. Mise hit all of these against NACS v0.1.3 (its spec 022, Phase 3):

- Retrying a denied connect with the same token loops forever (the pre-fix mise
  client hit a 20s test guard proving exactly that).
- A missing token (failed silent refresh) must not fall through to an anonymous
  connect, or an authenticated session silently loses its grants.
- NATS.Net captures auth credentials at connect time, so built-in reconnect
  (`MaxReconnectRetry`) re-presents a token that may have expired, and cannot
  recompute the scoped `InboxPrefix` for a new principal.

Mise's `NatsClientService` encodes the answers and verified them against a real
NACS container (`NatsClientServiceAuthTests`). Those semantics are currently
documented nowhere in NACS itself.

## Decision

The `AuthCallout.Client` managed connection (ADR 0020) adopts mise's semantics
as the contract:

1. **Transport failures** (server down, network): retry indefinitely with
   exponential backoff and jitter.
2. **`Authorization Violation`** (callout denied the token, detected by walking
   inner exceptions for the server error): invalidate the cached token, force a
   refresh through the token provider, retry **exactly once**, then fail
   terminally. Same token twice is pointless; fresh token once is the only
   recoverable case.
3. **No token available** on the authenticated path (provider returns null):
   fail immediately with a distinct exception. Never downgrade to an anonymous
   connect; anonymous is an explicit caller opt-in.
4. **Reconnects are owned by the library**, not NATS.Net: `MaxReconnectRetry = 0`
   plus a disconnect handler that re-enters the connect pipeline, so every
   reconnect fetches a current token from the provider and recomputes the
   scoped `InboxPrefix` from it.
5. **Token refresh is proactive**: re-mint a margin (~30s) before `exp`, keep
   the stale token and back off briefly on a failed re-mint rather than
   hammering the IdP per connect (promoted from the load sample's
   `RefreshingCalloutConnection`).

## Consequences

- Clients fail fast and loud on bad credentials (~1s with one retry) instead of
  spinning, and surface "re-login needed" to the caller.
- The token provider seam must support forced invalidation, not just reads.
- If a future NATS.Net adds a per-reconnect credential callback, rule 4's
  self-managed loop can collapse onto it; the seam stays internal so that swap
  is non-breaking.
- These semantics are proven behavior (mise integration suite) being codified,
  not new design.

## Related

- ADRs: [0003](./0003-resolution-order-issuer-validate-app-profile.md) (deny,
  never downgrade), [0020](./0020-ship-client-integration-libraries.md).
- Implementation: [P19 — Client library](../../implementation-plan/19-client-library.md).
- Prior art: mise `src/Mise.App/Mise.App.Core/Services/NatsClientService.cs`,
  mise spec `022-nacs-auth-callout` Phase 3/4.
