# 0019 — Empty grant sides mint an explicit deny-all

- **Status:** accepted
- **Date:** 2026-11-14
- **Deciders:** Ryan

## Context

[ADR 0003](./0003-resolution-order-issuer-validate-app-profile.md) and [doc 03](../design/03-resolution-pipeline.md#mint-and-respond) describe the minted NATS user JWT as carrying "publish/subscribe allow lists" built from a profile's (or the anonymous union's) static and templated subjects. Neither document says what happens when one side of that grant — publish or subscribe — is empty, and the obvious assumption (an empty allow list behaves like deny-all) is **wrong**.

Verified live against a real `nats-server` v2.15.3: its `buildPermissionsFromJwt` skips building a permission side at all when that side's allow *and* deny lists are both empty, which leaves the side **unrestricted**, not denied. Concretely, a subscribe-only grant — including an anonymous union composed solely of `anonymousProfile.subscribe` contributions, the pattern [ADR 0004](./0004-anonymous-grant-is-union-across-apps.md) and the securing-applications guide recommend — minted with only an `Allow` list on subscribe and nothing on publish could publish to **any** subject in its account, defeating the entire point of a subscribe-only grant. This was caught end to end in `dev/secure-example/`: before the fix, an anon client could forge messages into another app's public broadcast subject; after it, that delivery fails while legitimate subscribe-only traffic is unaffected.

## Decision

The minter builds each side of a grant (publish, subscribe) from its allow list, and whenever that list is **empty**, mints an explicit `deny: [">"]` for that side instead of leaving it empty.

- This applies independently per side: a grant with a non-empty publish allow and an empty subscribe allow gets an explicit subscribe deny-all, and vice versa.
- This is unconditional and applies to every minted grant — profile-based and anonymous-union — not just a configured opt-in.
- Implemented once in `UserJwtMinter.BuildPermission` (`AuthCallout.Nats`), shared by every grant-construction path, so the rule cannot be bypassed by a specific profile or the anonymous composer.
- Unit-tested directly against this behavior (e.g. `MintGrant_EmptyPublishList_MintsExplicitPublishDenyAll`) and proven against a real server in `dev/secure-example/`.

## Consequences

- A profile or anonymous contribution that only lists `subscribe` subjects now mints a genuinely publish-dead user, matching the intuitive (and previously assumed, but false) semantics of an "empty" side.
- No config schema change: this is a minting-time behavior, invisible to `anonymousProfile` / profile authors, who continue to write `publish` / `subscribe` lists as before.
- Any future change to grant construction must preserve this invariant — a regression here silently reopens the exact forgery the proof run demonstrates in `dev/secure-example/`.
- Does not change deny-by-default semantics for a **non-empty** allow list (unchanged: everything not listed on that side is already denied once the side has any allow entries).

## Related

- Design: [03 — Resolution pipeline](../design/03-resolution-pipeline.md#mint-and-respond) (mint-and-respond step this decision refines).
- ADRs: [0003](./0003-resolution-order-issuer-validate-app-profile.md) (grant construction), [0004](./0004-anonymous-grant-is-union-across-apps.md) (the subscribe-only anonymous pattern this protects).
- Proof: `dev/secure-example/` (delivery-level assertions against a real nats-server v2.15.3), `tests/AuthCallout.Nats.Tests/Callout/UserJwtMinterTests.cs`.
