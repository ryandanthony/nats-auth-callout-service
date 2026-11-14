# Protecting anonymous (pre-JWT) access

## How to protect anon traffic (TL;DR)

Anonymous clients all share one account and one permission set, so protection is entirely about what the minted grant contains. Do this:

1. **Quarantine them in a dedicated account.** Keep `anonymous.targetAccount` pointing at an account (`ANON`) used for nothing else — no services, no JetStream assets, no users. The account boundary blocks everything outside it (§3.1). Example:

   ```yaml
   # bootstrap.yaml — the callout mints every anonymous connection into ANON
   anonymous:
     targetAccount: ANON
     expiry: 5m
   ```

   ```conf
   # server.conf — ANON exists only as an empty quarantine account.
   # No users (the callout's minted JWT places clients here via its audience),
   # no exports, no JetStream, nothing shared with NOTES/TASKS/AUTH.
   accounts {
     AUTH: {
       users: [ { nkey: U...CALLOUT_SERVICE_USER } ]
     }
     NOTES: { ... }
     TASKS: { ... }
     ANON: {
       # empty on purpose — isolation comes from having nothing in here
       limits: { max_connections: 1000, max_subscriptions: 50, max_payload: 65536 }
     }
   }
   ```

   Anti-example: the dev loop's `server.conf` imports `$JS.API.>` / `$KV.>` from `AUTH` into `ANON` (a dev-only workaround). That import weakens the account boundary — only the minted grant's lack of those subjects keeps anon clients out (§2.4). A production `ANON` should have **no imports at all**.

2. **Keep the grant subscribe-only wherever possible — but deny publish explicitly.** Contribute only genuinely public broadcast subjects to `anonymousProfile` (e.g. `subscribe: [notes.public.>]`). Anon clients that cannot publish cannot message each other at all (§4.1). **Trap (verified live, v2.15.3):** an *empty or omitted* publish list is **not** deny-all — NATS treats "no publish permission at all" as *unrestricted publish* (`buildPermissionsFromJwt`: empty allow + empty deny ⇒ no publish restriction). A subscribe-only user happily published to arbitrary subjects until given `publish: { deny: [">"] }`. So a truly subscribe-only grant must carry an **explicit publish deny `>`**. ⚠️ This repo's `UserJwtMinter` currently sets only `Allow` lists — if every app's `anonymousProfile` is subscribe-only, the minted anon JWT has an empty publish allow and anon clients can publish anywhere inside `ANON`. The minter should set `Pub.Deny: [">"]` (and symmetrically `Sub.Deny`) whenever the corresponding union list is empty.
3. **Never grant `_INBOX.>` or any shared inbox root.** Inboxes have no server-side ownership — any subscriber to `_INBOX.>` reads everyone's replies in the account (§2.2, verified live).
4. **Don't overlap publish and subscribe subjects** in the union unless the subject is intentionally a public channel: overlap = every anon client sees every other's publishes (§2.1).
5. **No broad wildcards** (`>`, `*.>`) in any `anonymousProfile` — the grant is a union, so one app's mistake becomes every anon connection's permissions (§2.3, §4.4).
6. **Keep the anon expiry short** (`anonymous.expiry`, e.g. `5m`) so revocation and config changes take effect quickly.
7. **Cap the `ANON` account** in server config (`limits { max_connections, max_subscriptions, max_payload }`) to bound abuse (§3.6).
8. **If anon request-reply is ever needed**, use per-connection private inboxes: client picks a random ID, sets `inbox_prefix = _INBOX.anon.<id>`, callout sanitizes and mints `subscribe: _INBOX.anon.<id>.>`; responders use `allow_responses` (§5). Until then, anon request-reply stays unsupported by design.

Everything below is the research and evidence behind these rules.

---

How we keep anonymous NATS connections — clients that connect **before/without a bearer token** — from seeing each other's traffic, what NATS gives us out of the box, and the rules this service (and `anonymousProfile` authors) must follow.

Status: research notes + working guidance. Sources: NATS docs (`nats-io/nats.docs`), the versioned reference (`nats-io/nats.docs.v2`, cross-checked for 2.11–2.15), NATS by Example (auth section), and this repo's ADRs (0004, 0013) and design docs (02, 03).

Every server-behavior claim below was **verified empirically** (2026-11-14, `nats-server` v2.15.3 in Docker, `nats` CLI 0.4.0) with an `ANON`/`OTHER` accounts config mirroring this deployment:

1. Shared-subject echo: two same-account users with overlapping pub/sub on `chat.lobby` see each other's messages. ✔
2. Deny-by-default: a publish-only grant on `notes.inbound.>` rejects the matching subscribe (`Permissions Violation for Subscription`). ✔
3. Account isolation: a `>`-privileged user in `OTHER` never receives `ANON` traffic. ✔
4. Private inbox: `--inbox-prefix _INBOX.anon.joe` + `subscribe: _INBOX.anon.joe.>` completes request-reply end to end. ✔
5. The same user is denied `subscribe _INBOX.>`. ✔
6. ...and denied another user's prefix (`_INBOX.anon.pam.>`). ✔
7. Inbox snooping: a user granted `subscribe: _INBOX.>` receives *another user's* reply traffic verbatim. ✔
8. `allow_responses: true` lets a responder publish only reply subjects — an explicit publish to any other subject is a Publish Violation. ✔

---

## 1. What "anonymous" means here

With `auth_callout` configured, **every** client connection (including ones presenting no credentials at all) is routed through `$SYS.REQ.USER.AUTH` to this service — the only exception is the callout's own `auth_users` (the v2 reference: "All client connections requiring authentication will have their credentials pass-through" to the callout; with the block present, server-side user lists are not consulted). There is no unauthenticated side door: an anonymous client only gets whatever the minted user JWT says.

Operational caveat (v2 reference, 2.11–2.15): the `auth_callout` block is **not config-reloadable** — any change to it (including adding/removing it) aborts the *entire* `nats-server --signal reload` with "config reload not supported for AuthCallout", discarding every other change in the file. Plan callout config changes as server restarts.

Per ADR 0004 / `AnonymousComposer` (doc 03, Stage 0):

- An anonymous connection receives the **union of every application's `anonymousProfile`** (publish + subscribe lists, de-duplicated).
- It is minted into the dedicated **`ANON` account** (global `anonymous.targetAccount`), with a global expiry.
- It gets **no KV/JetStream subjects ever**, no token record, no assertion, `IdentityPropagationEnabled: false`.
- Zero contributions across all apps ⇒ **deny** (`AnonymousNoContributions`).

So "pre-JWT" clients are not a special weaker path — they are a normal minted user with a deliberately tiny grant.

## 2. The threat model: anon-to-anon visibility

All anonymous connections land in the *same* account (`ANON`) with the *same* permission set. Within one account, NATS pub/sub is a shared bus: **the only thing separating two users in the same account is their subject permissions.** Specific risks:

1. **Shared subject echo** — if the anon grant allows publish *and* subscribe on an overlapping subject (e.g. `chat.lobby`), every anon client sees every other anon client's publishes. Sometimes that's the point (public broadcast); it must never happen by accident.
2. **Inbox snooping** — `_INBOX.>` has **no server-side ownership semantics**; it is purely a client-side naming convention (verified against a real server, ADR 0013). Any client granted `subscribe: _INBOX.>` can read *all* request-reply responses in the account, including other clients'. The NATS authorization docs call this out explicitly ("any client with permission to subscribe to `_INBOX.>` can receive *all* responses published").
3. **Wildcard creep** — a careless `anonymousProfile` contributing `>` or a broad wildcard hands it to *every* anonymous connection, because the grant is a union (one app's mistake becomes everyone's exposure).
4. **Cross-account leakage via imports** — the dev `server.conf` imports `$JS.API.>` / `$KV.>` from `AUTH` into `ANON`. The minted anon grant never includes those subjects, so permissions still block it, but the import means the *account* boundary alone is not doing the work there — the per-user subject grant is.

## 3. What NATS supports out of the box

### 3.1 Accounts — the true isolation boundary

Accounts are complete, no-configuration isolation: subjects in one account are invisible to another unless explicitly exported/imported. We already use this (dedicated `ANON` account) so anonymous clients can never touch `NOTES` / `TASKS` / `AUTH` traffic regardless of their subject grant. The NATS docs recommend exactly this pattern ("*Accounts* allow complete isolation limiting what members of an account can see").

**What accounts do NOT give us:** isolation *between members of the same account*. Two anon clients in `ANON` are only separated by permissions.

### 3.2 Per-user publish/subscribe permissions (deny-by-default)

A minted user JWT carries explicit `pub`/`sub` allow (and optionally deny) lists. Once an allow list is present, **everything not listed is denied** — NATS user permissions are deny-by-default. This is the primary tool we already use: the anon union grant is the entire world an anonymous client can see.

Server-config equivalents for non-callout setups: `permissions` per user and `default_permissions` (applies to users without explicit permissions) in the `authorization` block.

### 3.3 Private inboxes (custom inbox prefix + scoped grant)

The canonical out-of-the-box pattern for stopping reply snooping inside a shared account (NATS by Example, "Private Inbox"):

1. Do **not** grant `subscribe: _INBOX.>`.
2. Each client connects with a **custom inbox prefix** (`inbox_prefix` / `NatsOpts.InboxPrefix` — supported by all maintained clients), e.g. `_INBOX_joe`.
3. Grant each user `subscribe: _INBOX_joe.>` only.

Result: a client can only receive replies addressed to *its own* prefix; subscribing to the generic `_INBOX.>` or another user's prefix is a permissions violation. This repo already applies this pattern for *authenticated* identity-propagation reads (`_INBOX.{app-slug}.{derived-principal-key}.>`, ADR 0013) — but note the derivation needs a principal, which anonymous connections don't have (see §5).

### 3.4 `allow_responses` — reply permission without `_INBOX.>`

For **responder** services: `allow_responses: true` (or `{ max: N, expires: "1m" }`) gives a temporary, implicit publish permission to exactly the reply subject of a request it received, and implicitly denies all other publishes. This lets a service answer requests without ever holding a standing `publish: _INBOX.>` grant. Caveat from the docs: the reply subject bypasses the publish allow/deny lists while the window is open, so don't use it where you need explicit control of reply subjects.

Relevant to us if we ever run a service *inside* the `ANON` account that answers anon requests. (The user-JWT equivalent is the `resp` field in minted permissions; the callout can set it.)

### 3.5 `no_auth_user` — the non-callout alternative

Without auth callout, the server-config way to handle credential-less clients is `no_auth_user: <user>`: unauthenticated connections are bound to a named user (in some account) whose permissions then apply. Notes from the docs:

- Doesn't work with nkeys or bcrypted passwords; the referenced user must be defined in the `authorization` or `accounts` block.
- The client must still complete the CONNECT handshake (auth timeout applies).
- Reload semantics (v2 reference): *removing* `no_auth_user` reloads; *setting or changing* it fails the reload.
- One wording trap in the newer docs: the request-reply learn page says the client "owns" the `_INBOX.` namespace. That is only a subject-collision reservation, **not** a security boundary — verified live (§2 item 2 / test T7 above): a same-account user granted `subscribe: _INBOX.>` reads other clients' replies.
- **Not relevant to our deployment**: with `auth_callout` configured, the callout handles credential-less connections itself, which is strictly better — we get a per-connection minted JWT, expiry, and events. Keep `no_auth_user` *out* of the server config so there is exactly one anon path.

### 3.6 Account limits (DoS containment)

Accounts support limits (max connections, payload, subscriptions). Capping `ANON` (e.g. `limits { max_connections: ... }` on the account in server config — keys are `max_connections` / `max_subscriptions` / `max_payload`, validated with `nats-server -t` on v2.15.3) bounds the blast radius of anonymous abuse. Orthogonal to visibility, but part of protecting anon access. Per the v2 reference, the per-account `max_subscriptions` under `accounts` **is** hot-reloadable and re-applies to already-connected clients (one exceeding the new limit is disconnected with `MaxSubscriptionsExceeded`); the effective value is `min(account limit, server max_subscriptions)`. Note the top-level `max_subscriptions` (per-client) is a different key and is *not* reloadable.

## 4. Rules for `anonymousProfile` authors (current model)

The union semantics of ADR 0004 make every contribution global to all anon clients. Enforceable guidance:

1. **Prefer subscribe-only contributions** to genuinely public broadcast subjects (the shipped samples do exactly this: `subscribe: [notes.public.>]`, no publish). An anon population that cannot publish cannot message each other, full stop.
2. **Never contribute `_INBOX.>`** (or any shared inbox root) to an `anonymousProfile`. ADR 0004's context mentions `_INBOX.>` as an example of a "useful" anon subject — with the ADR 0013 finding (no server-side inbox ownership) that example should be considered **obsolete and dangerous**: it would let every anon client read every reply in `ANON`.
3. **Avoid publish/subscribe overlap** unless the subject is intentionally a public broadcast channel. If anon clients may publish `foo.inbound.>`, don't also let them subscribe to it.
4. **No broad wildcards** (`>`, account-spanning `*.>`): one app's sloppy contribution becomes every anon connection's grant.
5. **Request-reply from anon clients is currently unsupported by design** — there is no safe account-wide inbox subject to grant (see §5 for what it would take).

## 5. Gap + possible future work: per-connection anon isolation

Today the anon grant is identical for every anonymous connection, and there's no per-connection subject in it. If we ever need anon request-reply (or per-client server-push), the out-of-the-box building blocks are:

- The authorization request gives the callout per-connection data: `user_nkey` (unique, server-generated, the subject of the minted JWT), `client_info.id`, and the client-supplied `connect_opts.name`.
- Minted permissions can embed any of these, e.g. `subscribe: _INBOX.anon.{X}.>`.

The catch: the **client must be able to compute its own inbox prefix** (`inbox_prefix` is set client-side before connecting), which rules out `user_nkey` and `client_info.id` (the client never learns them in time). The workable pattern:

> The anon client generates a random ID, sends it as its **connection name** (`connect_opts.name`), sets `inbox_prefix = _INBOX.anon.<id>`, and the callout sanitizes the name (same transform rules as ADR 0009: `require-safe` semantics — reject `.`, `*`, `>`, whitespace) and mints `subscribe: [_INBOX.anon.<sanitized-id>.>]` plus publish to the request subjects. Responders answer via `allow_responses` / reply-subject publish.

Collisions are possible (client-chosen IDs), but a colliding client can only receive replies for a prefix it deliberately chose — acceptable for anon-grade traffic, or mitigable by folding in a hash of `user_nkey`-adjacent data if we ever care. **Not implemented** — recorded here as the design-ready option if a use case shows up.

## 6. Summary

| Mechanism (out of the box) | What it isolates | Our status |
| --- | --- | --- |
| Accounts | Everything across accounts | ✅ dedicated `ANON` account (ADR 0004) |
| Per-user pub/sub allow lists (deny-by-default) | Everything not explicitly granted | ✅ minted anon union grant, no KV/JS ever |
| Custom inbox prefix + scoped `_INBOX.<x>.>` grant | Reply/inbox snooping within an account | ✅ for authenticated principals (ADR 0013); ❌ anon (no inbox granted at all — safe but no request-reply) |
| `allow_responses` | Responder reply permission without `_INBOX.>` | Not used; available if services live in `ANON` |
| `no_auth_user` | n/a (alternative anon path) | Intentionally unused — callout owns the anon path |
| Account limits | Anon resource abuse | ⚠️ not set on `ANON`; worth adding in server config |

Bottom line: **NATS gives no intra-account privacy except subject permissions.** Anonymous clients are kept from seeing each other by (a) being quarantined in `ANON`, (b) a deny-by-default minted grant that contains only the explicit `anonymousProfile` union, and (c) never granting shared inbox or overlapping pub/sub subjects to that union. The private-inbox pattern is the documented path if per-anon-connection request-reply is ever needed.

## 7. Secure-by-default configuration example (NATS + NACS)

A complete, minimal starting point: one app (`notes`), pre-authenticated (anonymous) users locked down with **no cross-talk**, and a realistic authenticated profile. The `server.conf` below passes `nats-server -t` on v2.15.3.

### 7.1 NATS `server.conf`

```conf
port: 4222

accounts {
  # The callout service's own account. Nothing else lives here.
  AUTH: {
    users: [ { nkey: UBSMQROM3HYWT3NULVSSGNQGIVUYCGDVTOKZYIYTQSPVGAHKMF4D3IWY } ]  # serviceUserSeed public key
    jetstream: enabled   # identity-store KV buckets (ADR 0013) live here
  }

  # One account per application. Declared empty: the minted user JWT's
  # audience places clients here; membership never lives in this file.
  NOTES: {}

  # Anonymous quarantine. No users, no imports, no exports, no JetStream.
  # Tight limits bound abuse (all three keys validated against v2.15.3).
  ANON: {
    limits: { max_connections: 500, max_subscriptions: 20, max_payload: 16384 }
  }
}

authorization {
  timeout: 3   # pre-auth clients must finish CONNECT within this window
  auth_callout {
    issuer: "AA7YGMNUDH3477U77LWTBMYZXKWN2FWLZ5AWAAATYZZNTYC2QZEN6KZA"   # issuerSeed public key
    account: AUTH
    auth_users: [ "UBSMQROM3HYWT3NULVSSGNQGIVUYCGDVTOKZYIYTQSPVGAHKMF4D3IWY" ]
  }
}
```

What makes it secure by default:

- **No `no_auth_user`** — the callout is the only path for credential-less clients, so a pre-authenticated connection can still authenticate (the server routes its bare CONNECT to `$SYS.REQ.USER.AUTH` and the callout mints the anon grant), but it gets *only* that grant.
- **No imports into `ANON`** — unlike the dev loop, nothing from `AUTH` (or anywhere) is reachable; the account boundary does its full job.
- **Every account except `AUTH` is empty** — there are no static passwords anywhere; compromise of this file leaks no credentials (both NKeys are public halves).
- **`timeout: 3`** keeps half-open unauthenticated sockets short-lived.

### 7.2 NACS `bootstrap.yaml` (anonymous settings)

```yaml
schemaVersion: 1
natsUrl: nats://nats:4222
issuerSeed:      { kind: env, name: NACS_ISSUER_SEED }       # signs minted JWTs
serviceUserSeed: { kind: env, name: NACS_SERVICE_USER_SEED } # callout's own connection
source:
  kind: files
  directory: /config/apps
anonymous:
  targetAccount: ANON   # must match the empty quarantine account above
  expiry: 5m            # short: anon grants age out quickly
  events:
    success: anon.auth.success   # emitted on the service's AUTH connection
    failure: anon.auth.failure
```

### 7.3 Application file — anonymous contribution with no cross-talk

```yaml
# /config/apps/Note.yaml
schemaVersion: 1
name: notes
targetAccount: NOTES
issuer:
  authority: https://keycloak.example.com/realms/prod
  jwksCache: 5m
  validateAudience: true
  audience: [nats]
  clockSkew: 60s
  allowedAlgorithms: [RS256]
match:
  azp: [notes-web]

# Anonymous union contribution: SUBSCRIBE-ONLY, one narrow public prefix.
# - no publish        -> anon clients cannot send anything to each other
# - no _INBOX.*       -> no reply snooping surface
# - no wildcard roots -> notes.public.> is the entire anon world
anonymousProfile:
  subscribe: [notes.public.>]
```

**No-cross-talk checklist for the anon union** (each verified in §TL;DR tests):

| Property | Why it holds here |
| --- | --- |
| Anon client A cannot message anon client B | No publish subjects in the union at all |
| A cannot read B's request replies | No `_INBOX.>`/inbox root granted; anon request-reply is unsupported by design (§4.5) |
| A cannot reach authenticated apps | Minted into `ANON`; `NOTES` is a different account with no exports |
| A cannot exhaust the server | `ANON` account limits + 5m expiry + `timeout: 3` |

> ⚠️ **One repo-level caveat (TL;DR #2):** because the minted JWT currently sets only allow lists, a publish list that ends up *empty* leaves publish unrestricted inside `ANON`. Until `UserJwtMinter` adds `Pub.Deny: [">"]` for empty unions, keep *at least one* app contributing a harmless, subscribe-disjoint publish subject (e.g. `publish: [notes.feedback.anon]`) so the allow list is non-empty — or apply the minter fix, which is the real secure-by-default answer.

### 7.4 Authenticated users — a realistic profile

The same `Note.yaml` continues with profiles for real users and services. Tokens come from Keycloak; routing is by `iss` + `azp`, permissions by role claims; per-tenant subjects are templated from token claims (ADR 0009):

```yaml
# /config/apps/Note.yaml (continued)
identityPropagation:
  enabled: true
events:
  success: notes.events.auth.success
  failure: notes.events.auth.failure
profiles:
  # Human users of the notes web app: scoped to their own household/tenant.
  - name: member
    match:
      realm_access.roles: [notes-user]
    nameClaim: preferred_username
    variables:
      - name: tenant
        claim: household_id
        required: true            # token without a tenant claim -> deny
        transforms: [lowercase, require-safe]
    publishTemplates:   [notes.{tenant}.commands.>]
    subscribeTemplates: [notes.{tenant}.events.>]
    grant:
      publish: []                      # no static publish — everything is tenant-templated
      subscribe: [notes.public.>]      # the only static subject: the public broadcast feed

  # The notes backend service (client-credentials token, matched on azp).
  - name: backend
    match:
      azp: [notes-backend]
    nameClaim: azp
    grant:
      publish:   [notes.*.events.>, "_INBOX.notes.>"]  # respond to any tenant; reply-publish only (weak capability, ADR 0013)
      subscribe: [notes.*.commands.>]
    autoAddSessionAccess: true    # auto-adds scoped $KV reads + per-principal inbox (ADR 0013)
```

Why this is realistic and safe:

- **Members** never hold a static account-wide subject: every grant line is templated from their own `household_id`, so tenant A's user physically cannot subscribe to tenant B's events (deny-by-default + no matching allow).
- `require-safe` rejects token values containing `.` `*` `>` or whitespace, so a hostile claim can't smuggle a wildcard into the subject space.
- **The backend service** gets cross-tenant `notes.*.commands.>` subscribe (it serves everyone) but its inbox capability is *publish-only* (`_INBOX.notes.>`): it can answer requesters, never read anyone's replies — the asymmetric rule from ADR 0013.
- **Anonymous ↔ authenticated separation is structural**: members and the backend are minted into `NOTES`, anon into `ANON`. The only overlap is `notes.public.>`, which members can also read — and only the backend (not members, not anon) can publish to, because nobody else has a matching publish allow.
