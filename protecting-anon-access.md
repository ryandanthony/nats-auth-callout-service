# Protecting anonymous (pre-JWT) access

How we keep anonymous NATS connections — clients that connect **before/without
a bearer token** — from seeing each other's traffic, what NATS gives us out of
the box, and the rules this service (and `anonymousProfile` authors) must
follow.

Status: research notes + working guidance. Sources: NATS docs
(`nats-io/nats.docs`), the versioned reference (`nats-io/nats.docs.v2`,
cross-checked for 2.11–2.15), NATS by Example (auth section), and this
repo's ADRs (0004, 0013) and design docs (02, 03).

Every server-behavior claim below was **verified empirically** (2026-11-14,
`nats-server` v2.15.3 in Docker, `nats` CLI 0.4.0) with an `ANON`/`OTHER`
accounts config mirroring this deployment:

1. Shared-subject echo: two same-account users with overlapping pub/sub on
   `chat.lobby` see each other's messages. ✔
2. Deny-by-default: a publish-only grant on `notes.inbound.>` rejects the
   matching subscribe (`Permissions Violation for Subscription`). ✔
3. Account isolation: a `>`-privileged user in `OTHER` never receives `ANON`
   traffic. ✔
4. Private inbox: `--inbox-prefix _INBOX.anon.joe` + `subscribe:
   _INBOX.anon.joe.>` completes request-reply end to end. ✔
5. The same user is denied `subscribe _INBOX.>`. ✔
6. ...and denied another user's prefix (`_INBOX.anon.pam.>`). ✔
7. Inbox snooping: a user granted `subscribe: _INBOX.>` receives *another
   user's* reply traffic verbatim. ✔
8. `allow_responses: true` lets a responder publish only reply subjects —
   an explicit publish to any other subject is a Publish Violation. ✔

---

## 1. What "anonymous" means here

With `auth_callout` configured, **every** client connection (including ones
presenting no credentials at all) is routed through `$SYS.REQ.USER.AUTH` to
this service — the only exception is the callout's own `auth_users` (the
v2 reference: "All client connections requiring authentication will have
their credentials pass-through" to the callout; with the block present,
server-side user lists are not consulted). There is no unauthenticated side
door: an anonymous client only gets whatever the minted user JWT says.

Operational caveat (v2 reference, 2.11–2.15): the `auth_callout` block is
**not config-reloadable** — any change to it (including adding/removing it)
aborts the *entire* `nats-server --signal reload` with "config reload not
supported for AuthCallout", discarding every other change in the file. Plan
callout config changes as server restarts.

Per ADR 0004 / `AnonymousComposer` (doc 03, Stage 0):

- An anonymous connection receives the **union of every application's
  `anonymousProfile`** (publish + subscribe lists, de-duplicated).
- It is minted into the dedicated **`ANON` account** (global
  `anonymous.targetAccount`), with a global expiry.
- It gets **no KV/JetStream subjects ever**, no token record, no assertion,
  `IdentityPropagationEnabled: false`.
- Zero contributions across all apps ⇒ **deny** (`AnonymousNoContributions`).

So "pre-JWT" clients are not a special weaker path — they are a normal minted
user with a deliberately tiny grant.

## 2. The threat model: anon-to-anon visibility

All anonymous connections land in the *same* account (`ANON`) with the *same*
permission set. Within one account, NATS pub/sub is a shared bus: **the only
thing separating two users in the same account is their subject permissions.**
Specific risks:

1. **Shared subject echo** — if the anon grant allows publish *and* subscribe
   on an overlapping subject (e.g. `chat.lobby`), every anon client sees every
   other anon client's publishes. Sometimes that's the point (public
   broadcast); it must never happen by accident.
2. **Inbox snooping** — `_INBOX.>` has **no server-side ownership semantics**;
   it is purely a client-side naming convention (verified against a real
   server, ADR 0013). Any client granted `subscribe: _INBOX.>` can read *all*
   request-reply responses in the account, including other clients'. The NATS
   authorization docs call this out explicitly ("any client with permission to
   subscribe to `_INBOX.>` can receive *all* responses published").
3. **Wildcard creep** — a careless `anonymousProfile` contributing `>` or a
   broad wildcard hands it to *every* anonymous connection, because the grant
   is a union (one app's mistake becomes everyone's exposure).
4. **Cross-account leakage via imports** — the dev `server.conf` imports
   `$JS.API.>` / `$KV.>` from `AUTH` into `ANON`. The minted anon grant never
   includes those subjects, so permissions still block it, but the import
   means the *account* boundary alone is not doing the work there — the
   per-user subject grant is.

## 3. What NATS supports out of the box

### 3.1 Accounts — the true isolation boundary

Accounts are complete, no-configuration isolation: subjects in one account
are invisible to another unless explicitly exported/imported. We already use
this (dedicated `ANON` account) so anonymous clients can never touch `NOTES`
/ `TASKS` / `AUTH` traffic regardless of their subject grant. The NATS docs
recommend exactly this pattern ("*Accounts* allow complete isolation limiting
what members of an account can see").

**What accounts do NOT give us:** isolation *between members of the same
account*. Two anon clients in `ANON` are only separated by permissions.

### 3.2 Per-user publish/subscribe permissions (deny-by-default)

A minted user JWT carries explicit `pub`/`sub` allow (and optionally deny)
lists. Once an allow list is present, **everything not listed is denied** —
NATS user permissions are deny-by-default. This is the primary tool we
already use: the anon union grant is the entire world an anonymous client can
see.

Server-config equivalents for non-callout setups: `permissions` per user and
`default_permissions` (applies to users without explicit permissions) in the
`authorization` block.

### 3.3 Private inboxes (custom inbox prefix + scoped grant)

The canonical out-of-the-box pattern for stopping reply snooping inside a
shared account (NATS by Example, "Private Inbox"):

1. Do **not** grant `subscribe: _INBOX.>`.
2. Each client connects with a **custom inbox prefix** (`inbox_prefix` /
   `NatsOpts.InboxPrefix` — supported by all maintained clients), e.g.
   `_INBOX_joe`.
3. Grant each user `subscribe: _INBOX_joe.>` only.

Result: a client can only receive replies addressed to *its own* prefix;
subscribing to the generic `_INBOX.>` or another user's prefix is a
permissions violation. This repo already applies this pattern for
*authenticated* identity-propagation reads
(`_INBOX.{app-slug}.{derived-principal-key}.>`, ADR 0013) — but note the
derivation needs a principal, which anonymous connections don't have (see §5).

### 3.4 `allow_responses` — reply permission without `_INBOX.>`

For **responder** services: `allow_responses: true` (or
`{ max: N, expires: "1m" }`) gives a temporary, implicit publish permission to
exactly the reply subject of a request it received, and implicitly denies all
other publishes. This lets a service answer requests without ever holding a
standing `publish: _INBOX.>` grant. Caveat from the docs: the reply subject
bypasses the publish allow/deny lists while the window is open, so don't use
it where you need explicit control of reply subjects.

Relevant to us if we ever run a service *inside* the `ANON` account that
answers anon requests. (The user-JWT equivalent is the `resp` field in minted
permissions; the callout can set it.)

### 3.5 `no_auth_user` — the non-callout alternative

Without auth callout, the server-config way to handle credential-less clients
is `no_auth_user: <user>`: unauthenticated connections are bound to a named
user (in some account) whose permissions then apply. Notes from the docs:

- Doesn't work with nkeys or bcrypted passwords; the referenced user must be
  defined in the `authorization` or `accounts` block.
- The client must still complete the CONNECT handshake (auth timeout applies).
- Reload semantics (v2 reference): *removing* `no_auth_user` reloads;
  *setting or changing* it fails the reload.
- One wording trap in the newer docs: the request-reply learn page says the
  client "owns" the `_INBOX.` namespace. That is only a subject-collision
  reservation, **not** a security boundary — verified live (§2 item 2 / test
  T7 above): a same-account user granted `subscribe: _INBOX.>` reads other
  clients' replies.
- **Not relevant to our deployment**: with `auth_callout` configured, the
  callout handles credential-less connections itself, which is strictly
  better — we get a per-connection minted JWT, expiry, and events. Keep
  `no_auth_user` *out* of the server config so there is exactly one anon path.

### 3.6 Account limits (DoS containment)

Accounts support limits (max connections, payload, subscriptions). Capping
`ANON` (e.g. `limits { conn: ... }` on the account in server config) bounds
the blast radius of anonymous abuse. Orthogonal to visibility, but part of
protecting anon access. Per the v2 reference, the per-account
`max_subscriptions` under `accounts` **is** hot-reloadable and re-applies to
already-connected clients (one exceeding the new limit is disconnected with
`MaxSubscriptionsExceeded`); the effective value is
`min(account limit, server max_subscriptions)`. Note the top-level
`max_subscriptions` (per-client) is a different key and is *not* reloadable.

## 4. Rules for `anonymousProfile` authors (current model)

The union semantics of ADR 0004 make every contribution global to all anon
clients. Enforceable guidance:

1. **Prefer subscribe-only contributions** to genuinely public broadcast
   subjects (the shipped samples do exactly this:
   `subscribe: [notes.public.>]`, no publish). An anon population that cannot
   publish cannot message each other, full stop.
2. **Never contribute `_INBOX.>`** (or any shared inbox root) to an
   `anonymousProfile`. ADR 0004's context mentions `_INBOX.>` as an example of
   a "useful" anon subject — with the ADR 0013 finding (no server-side inbox
   ownership) that example should be considered **obsolete and dangerous**:
   it would let every anon client read every reply in `ANON`.
3. **Avoid publish/subscribe overlap** unless the subject is intentionally a
   public broadcast channel. If anon clients may publish `foo.inbound.>`,
   don't also let them subscribe to it.
4. **No broad wildcards** (`>`, account-spanning `*.>`): one app's sloppy
   contribution becomes every anon connection's grant.
5. **Request-reply from anon clients is currently unsupported by design** —
   there is no safe account-wide inbox subject to grant (see §5 for what it
   would take).

## 5. Gap + possible future work: per-connection anon isolation

Today the anon grant is identical for every anonymous connection, and there's
no per-connection subject in it. If we ever need anon request-reply (or
per-client server-push), the out-of-the-box building blocks are:

- The authorization request gives the callout per-connection data:
  `user_nkey` (unique, server-generated, the subject of the minted JWT),
  `client_info.id`, and the client-supplied `connect_opts.name`.
- Minted permissions can embed any of these, e.g.
  `subscribe: _INBOX.anon.{X}.>`.

The catch: the **client must be able to compute its own inbox prefix**
(`inbox_prefix` is set client-side before connecting), which rules out
`user_nkey` and `client_info.id` (the client never learns them in time). The
workable pattern:

> The anon client generates a random ID, sends it as its **connection name**
> (`connect_opts.name`), sets `inbox_prefix = _INBOX.anon.<id>`, and the
> callout sanitizes the name (same transform rules as ADR 0009:
> `require-safe` semantics — reject `.`, `*`, `>`, whitespace) and mints
> `subscribe: [_INBOX.anon.<sanitized-id>.>]` plus publish to the request
> subjects. Responders answer via `allow_responses` / reply-subject publish.

Collisions are possible (client-chosen IDs), but a colliding client can only
receive replies for a prefix it deliberately chose — acceptable for
anon-grade traffic, or mitigable by folding in a hash of `user_nkey`-adjacent
data if we ever care. **Not implemented** — recorded here as the design-ready
option if a use case shows up.

## 6. Summary

| Mechanism (out of the box) | What it isolates | Our status |
| --- | --- | --- |
| Accounts | Everything across accounts | ✅ dedicated `ANON` account (ADR 0004) |
| Per-user pub/sub allow lists (deny-by-default) | Everything not explicitly granted | ✅ minted anon union grant, no KV/JS ever |
| Custom inbox prefix + scoped `_INBOX.<x>.>` grant | Reply/inbox snooping within an account | ✅ for authenticated principals (ADR 0013); ❌ anon (no inbox granted at all — safe but no request-reply) |
| `allow_responses` | Responder reply permission without `_INBOX.>` | Not used; available if services live in `ANON` |
| `no_auth_user` | n/a (alternative anon path) | Intentionally unused — callout owns the anon path |
| Account limits | Anon resource abuse | ⚠️ not set on `ANON`; worth adding in server config |

Bottom line: **NATS gives no intra-account privacy except subject
permissions.** Anonymous clients are kept from seeing each other by (a) being
quarantined in `ANON`, (b) a deny-by-default minted grant that contains only
the explicit `anonymousProfile` union, and (c) never granting shared inbox or
overlapping pub/sub subjects to that union. The private-inbox pattern is the
documented path if per-anon-connection request-reply is ever needed.
