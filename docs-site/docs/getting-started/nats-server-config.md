---
sidebar_position: 2
---

# NATS server configuration

The NATS server delegates every `CONNECT` to this service via [auth
callout](https://docs.nats.io/running-a-nats-service/configuration/securing_nats/auth_callout).
The callout runs in its own dedicated NATS **account** — never sharing an
account with the applications it authorizes — so that only its own
service-user connection can subscribe to `$SYS.REQ.USER.AUTH` (see
[docs/design/08-technical-architecture-csharp.md](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/design/08-technical-architecture-csharp.md)
§"Why the auth service runs in its own account").

## Minimal `server.conf`

```hocon
accounts {
  AUTH: {
    jetstream: enabled   # the callout's own KV writes (token/assertion/keys buckets)
    users: [ { nkey: <SERVICE_USER_PUBLIC_NKEY> } ]
    exports: [
      { service: "$JS.API.>" }
      { stream: "$KV.>" }
    ]
  }
  NOTES: {
    imports: [
      { service: { account: AUTH, subject: "$JS.API.>" } }
      { stream: { account: AUTH, subject: "$KV.>" } }
    ]
  }
  # one target account per application (see targetAccount in the config reference),
  # plus the global anonymous account (anonymous.targetAccount in the bootstrap file)
}

authorization {
  auth_callout {
    issuer: "<ISSUER_SEED_PUBLIC_NKEY>"     # public half of bootstrap issuerSeed
    account: AUTH
    auth_users: [ "<SERVICE_USER_PUBLIC_NKEY>" ]
    # xkey: "<XKEY_PUBLIC_NKEY>"            # optional: encrypted callout (ADR 0018)
  }
}
```

This is a trimmed version of the working dev config at
[`dev/AppHost.Local/nats/server.conf`](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/dev/AppHost.Local/nats/server.conf),
**verified live** end to end (see [`dev/README.md`](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/dev/README.md)).

## Why every target account imports `$JS.API.>` and `$KV.>` from `AUTH`

The callout's identity-store KV buckets (discovery, keys, tokens,
assertions — [doc 07](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/design/07-kv-storage.md))
are created via the callout's **own** `AUTH`-account connection, but real
clients connect into their target accounts (`NOTES`, `TASKS`, `ANON`, ...).
NATS account isolation makes JetStream streams invisible cross-account by
default, so without this export/import a client's minted grant subjects
(`$KV.<bucket>.<key>`) are correct but every read still fails with "stream
not found". Export `$JS.API.>` (service) and `$KV.>` (stream) from `AUTH`,
and import both into every target account.

**Do not** also enable `jetstream: enabled` on a target account that only
consumes the `AUTH` import: a target account's own local JetStream domain
shadows the `$JS.API.>` import, silently breaking every cross-account KV
read.

## Which NKey goes where

| Value | Comes from | Used as |
|-------|------------|---------|
| Issuer seed (private) | bootstrap `issuerSeed` | Signs minted user JWTs and the callout response. |
| Issuer public key | derived from issuer seed | `auth_callout.issuer` on the server. |
| Service user seed (private) | bootstrap `serviceUserSeed` | The callout's own NATS connection credential. |
| Service user public NKey | derived from service user seed | `accounts.AUTH.users[].nkey` and `auth_callout.auth_users` on the server. |
| xkey seed (private, optional) | bootstrap `xkeySeed` | Decrypts/encrypts the callout wire payload (ADR 0018). |
| xkey public key (optional) | derived from xkey seed | `auth_callout.xkey` on the server. |

Derive public halves from a seed with the [`nats`
CLI](https://github.com/nats-io/natscli): `nats auth nkey show <seed>`.

## Next

[Run the service](./running-the-service) once the server config, target
accounts, and a bootstrap file are in place.
