# 0004 — Anonymous grant is the union across applications

- **Status:** proposed
- **Date:** 2026-09-05
- **Deciders:** Ryan

## Context

An anonymous connection carries no token, so it cannot be routed to any single application by issuer or claim. It still needs a defined, useful set of permissions (for example bootstrap and `_INBOX.>` subjects), and that set should track the applications actually configured.

> **Amendment (2026-11-14):** the `_INBOX.>` example above is obsolete and dangerous — see the note at the end of this ADR.

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
- ADRs: [0003](./0003-resolution-order-issuer-validate-app-profile.md) (no-token branch grants this union), [0005](./0005-config-abstraction-and-sources.md) (add/remove-an-app semantics), [0007](./0007-internal-configuration-model.md) (`anonymousProfile` field), [0013](./0013-kv-bucket-naming-contract.md) (`_INBOX` ownership finding).

## Amendment (2026-11-14)

The Context section above cites `_INBOX.>` as an example of a "useful" anonymous subject. That example is **obsolete and dangerous** and must not be followed:

- `_INBOX.>` has **no server-side ownership semantics** — it is purely a client-side naming convention. Verified live against `nats-server` v2.15.3: a client granted `subscribe: _INBOX.>` receives *every other client's* request-reply responses in the account, not just its own. This is the same finding already recorded in [ADR 0013](./0013-kv-bucket-naming-contract.md) for authenticated grants, and it applies identically here.
- Because the anonymous grant is a **union** across every application's `anonymousProfile` (this ADR's Decision), a single app contributing `_INBOX.>` (or any shared inbox root) would hand every anonymous connection the ability to read every other anonymous connection's replies. The correct pattern, if anonymous request-reply is ever needed, is a per-connection scoped prefix (e.g. `_INBOX.anon.{client-chosen-id}.>`), never the account-wide root — see `dev/secure-example/` for a worked, proven example of the quarantine pattern this ADR's `ANON` account depends on.
- Separately, accounts isolate fully: an anonymous connection minted into `ANON` receives **nothing** of an application's traffic unless that application explicitly exports the subject and `ANON` explicitly imports it. This was proven end-to-end in `dev/secure-example/` (a missing `notes.public.>` export/import pair meant anon broadcasts silently never arrived until added). Nothing in this ADR should be read as implying the account boundary alone, or mere co-location of config, grants anonymous connections visibility into an application's account.

This amendment does not change the Decision (the union mechanism, the dedicated `ANON` account, and global expiry all stand) — it corrects the Context's example and makes the account-isolation requirement explicit. `anonymousProfile` authors: never contribute `_INBOX.>` or any shared inbox root.
