---
sidebar_position: 7
---

# Securing applications

How to correctly secure one application on this callout, and how to extend that to two (or more) applications sharing the same NATS server without one app's mistake leaking into another's. Grounds: [ADR 0001](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0001-named-permission-profiles.md) (profiles), [ADR 0004](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0004-anonymous-grant-is-union-across-apps.md) (anonymous union), [ADR 0013](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0013-kv-bucket-naming-contract.md) (inbox ownership), [ADR 0016](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0016-token-validation-parameters.md) (shared-issuer consistency).

A runnable, empirically verified proof of everything below lives at [`dev/secure-example/`](https://github.com/ryandanthony/nats-auth-callout-service/tree/main/dev/secure-example) in this repo: a real `nats-server`, a real Keycloak, the real callout host, and a C# proof app (`Prove.cs`) that asserts 24 properties by actual message delivery (not CLI exit codes — an async permissions violation still looks like a successful publish to the client) across two applications. Every claim in this guide traces back to that example or to a live NATS server test against `nats-server` v2.15.3.

## Securing one application

### Give it its own account, declared empty

Each application gets a dedicated NATS account, declared with no static users in `server.conf`. Membership travels via the minted user JWT's audience, not via server-side user entries — the callout mints the connection directly into the app's account.

```conf
accounts {
  AUTH: {
    users: [ { nkey: U...CALLOUT_SERVICE_USER } ]
    jetstream: enabled
  }
  NOTES: { }   # empty: the minted JWT's account claim puts clients here
}

authorization {
  timeout: 3
  auth_callout {
    issuer: "AB...ISSUER_NKEY"
    account: AUTH
    auth_users: [ "U...CALLOUT_SERVICE_USER" ]
  }
}
```

Accounts are the strong isolation boundary: subjects in one account are invisible to another unless explicitly exported/imported. A token minted for `NOTES` can never yield subjects in any other account.

### Deny by default, and know the empty-allow-list trap

Permission profiles ([ADR 0001](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0001-named-permission-profiles.md)) grant only the publish/subscribe subjects a profile lists; everything else is denied.

**Verified live against `nats-server` v2.15.3:** an *empty or omitted* publish (or subscribe) list on a user permission set is **not** deny-all. The server treats "no permission entries at all" on one side as *unrestricted* on that side — a subscribe-only grant with an empty publish list could still publish to arbitrary subjects, because the server's permission builder only restricts a side once it sees at least one allow or deny entry on it.

This repo's `UserJwtMinter` closes that gap for you: for whichever side (publish or subscribe) a profile's grant resolves to empty, the minter adds an explicit `deny: [">"]` on that side before signing the JWT. This is unit-tested and proven end to end in `dev/secure-example/` (the proof's anon-to-anon forgery check specifically exercises this fix). You don't need to work around the trap yourself — but understand why it exists: if you ever mint JWTs by another path, or hand-write a `nats-server` config outside this service, an empty allow list is **not** a safe default on its own; it needs an explicit deny.

### Design the anonymous contribution narrowly

Any application may contribute an `anonymousProfile` to the global anonymous grant ([ADR 0004](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0004-anonymous-grant-is-union-across-apps.md)). Keep it minimal:

- **Subscribe-only.** Anonymous clients that cannot publish cannot message each other at all.
- **Narrow, static public prefixes** — e.g. `notes.public.>` — never a bare `>` or broad wildcard like `*.>`. The contribution is unioned across every configured app, so one app's sloppy wildcard becomes every anonymous connection's permission.
- **Never `_INBOX.>`.** `_INBOX` has no server-side ownership semantics at all — it's purely a client-side naming convention. Any client granted `subscribe: _INBOX.>` reads every other client's request-reply traffic in the account, verbatim. Verified live against a real server.
- **No publish/subscribe overlap**, unless the subject is intentionally a public broadcast channel. Overlap on the same subject means every anonymous client sees every other anonymous client's publishes.

```yaml
anonymousProfile:
  subscribe: [notes.public.>]
```

### Design member and service profiles deliberately

- **Role-matched member profiles** use a claim match (e.g. `realm_access.roles: [notes-user]`) and build tenant-scoped subjects from token claims via templated variables ([ADR 0009](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0009-templated-subject-variables.md)). Always run a tenant-id variable through `require-safe` (or another sanitizing transform) so an attacker-controlled claim value can never inject NATS wildcard or separator characters (`.`, `*`, `>`) into a minted subject.

  ```yaml
  profiles:
    - name: member
      match:
        realm_access.roles: [notes-user]
      nameClaim: preferred_username
      variables:
        - name: tenant
          claim: household_id
          required: true
          transforms: [lowercase, require-safe]
      publishTemplates: ["notes.{tenant}.commands.>"]
      subscribeTemplates: ["notes.{tenant}.events.>"]
      grant:
        publish: []
        subscribe: [notes.public.>]
  ```

- **Service profiles match on `azp`** (the client-credentials client id) by default, not on a role claim — services get least-privilege scoped subjects, never a blanket grant.

- **Inbox asymmetry.** A responder service that answers request-reply needs to *publish* replies: grant it `publish: ["_INBOX.{app-slug}.>"]`. That's safe because publishing to an inbox can only send a reply, not read anyone's traffic. Never grant the account-wide `_INBOX.>` subscribe to anyone, including a service — that's the leak described above, and it applies to authenticated principals exactly as it does to anonymous ones ([ADR 0013](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0013-kv-bucket-naming-contract.md)).

  ```yaml
    - name: backend
      match:
        azp: [notes-backend]
      nameClaim: azp
      grant:
        publish: [notes.*.events.>, notes.public.>, "_INBOX.notes.>"]
        subscribe: [notes.*.commands.>]
  ```

### Operational hardening

| Setting | Where | Guidance |
|---|---|---|
| Anonymous expiry | `anonymous.expiry` in bootstrap | Keep it short (e.g. `5m`, or even `15s` for aggressive revocation). Verified live: the server enforces a minted JWT's expiry on an **open** connection — with a 15s expiry, an anonymous subscriber was disconnected by the server exactly 15 seconds after connecting, forcing a fresh pass through the callout on reconnect. |
| Account limits | `server.conf`, inside the account block | Use `limits: { max_connections, max_subscriptions, max_payload }` — these are the exact key names, validated by running `nats-server -t` against the config. Cap every app account, not just `ANON`. |
| Auth timeout | `authorization { timeout: N }` | Bounds how long the server waits for the callout round trip before rejecting a connection. |
| `no_auth_user` | `server.conf` | Don't set it. It creates an unauthenticated side door that bypasses the callout entirely; with `auth_callout` configured, every connection — including ones presenting no credentials — should go through `$SYS.REQ.USER.AUTH`. |
| `auth_callout` reloadability | `server.conf` | Not config-reloadable. Any change to the `auth_callout` block (including adding or removing it) aborts the *entire* `nats-server --signal reload`, discarding every other change in the file. Plan callout config changes as a server restart, not a hot reload. |

```conf
ANON: {
  limits: { max_connections: 500, max_subscriptions: 20, max_payload: 16384 }
}
```

## Securing two applications on one NATS server

Everything above still applies per app. Two more concerns show up once a second application shares the server.

### Route by `azp` within one issuer, and keep issuer blocks identical

Two applications can share one OIDC issuer and be routed purely by a claim — typically `azp` — rather than by issuer. When applications **do** share an `authority`, their `issuer` blocks (clock skew, allowed algorithms, audience, audience validation) must be byte-for-byte identical ([ADR 0016](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0016-token-validation-parameters.md)) — config-load validation rejects a config where two apps share an authority but diverge in any issuer field, because token validation runs once per shared authority and a divergent block would make effective strictness depend on candidate ordering.

```yaml
# apps/Note.yaml and apps/Task.yaml both declare this issuer block, verbatim
issuer:
  authority: http://localhost:8903/realms/secure-example
  jwksCache: 5m
  validateAudience: true
  audience: [nats]
  clockSkew: 60s
  allowedAlgorithms: [RS256]
match:
  azp: [notes-web, notes-backend]   # Note.yaml
# match:
#   azp: [tasks-cli, tasks-backend] # Task.yaml
```

### One account per app is structural isolation

Give each app its own NATS account (`NOTES`, `TASKS`, ...). A token minted for one application can never yield another application's subjects — this is not a permissions detail that could be misconfigured away, it's the account boundary itself ([ADR 0001](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0001-named-permission-profiles.md)). Two apps sharing an issuer and even a Keycloak realm are still fully isolated at the NATS layer.

### The anonymous union spans every app

The anonymous grant is the **union** of every configured application's `anonymousProfile`, de-duplicated, minted into one global `ANON` account ([ADR 0004](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0004-anonymous-grant-is-union-across-apps.md)). This has a sharp edge: the union is global to *all* anonymous clients, across *all* apps. If app A contributes a careless `>` or a publish/subscribe overlap, every anonymous connection gets it — including ones that never touch app A's domain. Review each app's `anonymousProfile` as if it applies to the whole fleet, because it does.

```yaml
# Task.yaml's contribution
anonymousProfile:
  subscribe: [tasks.public.>]
```

```text
Effective ANON grant with both Note.yaml and Task.yaml configured:
  subscribe: [notes.public.>, tasks.public.>]
```

### Cross-account exports: the one sanctioned crack

Accounts isolate fully by default: with no export/import, an anonymous subscriber in `ANON` can never receive traffic published inside `NOTES` or `TASKS`, even traffic the app intends to broadcast publicly. This was confirmed empirically while building the proof. The one sanctioned way through the wall is each app account **exporting** its own public broadcast feed as a stream, imported into `ANON` only:

```conf
accounts {
  NOTES: {
    exports: [ { stream: "notes.public.>" } ]
  }
  TASKS: {
    exports: [ { stream: "tasks.public.>" } ]
  }
  ANON: {
    imports: [
      { stream: { account: NOTES, subject: "notes.public.>" } }
      { stream: { account: TASKS, subject: "tasks.public.>" } }
    ]
    limits: { max_connections: 500, max_subscriptions: 20, max_payload: 16384 }
  }
}
```

Rules for this crack:

- Export only the narrow public-broadcast subject, never a wildcard that could later cover something private.
- `ANON` imports from every app's public export; app accounts must **not** import each other's feeds. `NOTES` must never import from `TASKS` or vice versa — that would reopen cross-app leakage through a path this guide otherwise closes off entirely.
- The export/import is additive to the minted grant, not a replacement for it: an anonymous subscriber still needs `notes.public.>` in its minted `subscribe` list (via the union) to actually receive the imported stream.

### Cross-app leakage checklist

Before shipping a second application, confirm each of these (mirrors `Prove.cs` checks 7–9 in `dev/secure-example/`, verified there by actual message delivery, not just CLI exit codes):

- [ ] The new app's token routes to its own account without colliding with the first app's `azp`/claim match (two apps matching the same token is a config-load ambiguity, denied outright).
- [ ] A principal of app A cannot **observe** any subject in app B's account (subscribe attempts across accounts fail with no delivery).
- [ ] A principal of app A cannot **inject** into any subject in app B's account (publish attempts across accounts fail with no delivery), including each other's public broadcast subjects — the exports go to `ANON` only, not to each other.
- [ ] Neither app's backend/service profile can publish or subscribe into the other app's account.
- [ ] The anonymous union correctly spans both apps: an anonymous client receives *both* apps' public broadcasts and can forge into neither.
- [ ] A team/tenant boundary within one app (e.g. `tasks.ops7.*` vs. `tasks.dev1.*`) still holds even though the server also hosts a second, unrelated app.

`dev/secure-example/` is the runnable version of this checklist: `cd dev/secure-example && dotnet run Prove.cs` against the stack described in its README exercises every item above end to end and reports `RESULT: 24 passed, 0 failed`.
