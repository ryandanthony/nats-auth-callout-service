# secure-example — proof of the secure-by-default configuration

Runnable proof of the secure-by-default configuration from the
[securing-applications guide](../../docs-site/docs/guides/securing-applications.md):
real `nats-server`, real
Keycloak, the real `AuthCallout.Host`, and a C# single-file proof app
(`Prove.cs`) that asserts 24 security properties across **two applications**
by **actual message delivery** (not CLI exit codes — an async permissions
violation still looks like a successful publish to the client).

## Layout

| File | Purpose |
| --- | --- |
| `server.conf` | Secure-by-default NATS config: `AUTH` + per-app `NOTES`/`TASKS` + quarantined `ANON` with limits, `auth_callout`, no `no_auth_user`. Each app account's only export is its public feed (`notes.public.>` / `tasks.public.>`) into `ANON`. |
| `bootstrap.yaml` | NACS bootstrap: anon → `ANON`, 5m expiry, files source, `allowInsecureHttp` for the local Keycloak. |
| `apps/Note.yaml` | First application: subscribe-only `anonymousProfile`, tenant-templated `member` profile (`household_id`), `azp`-matched `backend` profile. |
| `apps/Task.yaml` | Second application: same issuer, routed by `azp` (`tasks-cli`/`tasks-backend`), own `TASKS` account, team-templated `worker` profile (`team_id`), contributes `tasks.public.>` to the anon union (ADR 0004). |
| `realm/secure-example-realm.json` | Keycloak realm: clients `notes-web`, `notes-backend`, `tasks-cli`, `tasks-backend`; users `alice` (household `Fam42`), `bob` (`Fam99`), `carol` (team `Ops7`). |
| `Prove.cs` | .NET 10 file-based proof app (NATS.Net). `dotnet run Prove.cs`. |
| `.issuer.seed` `.service.seed` `.assertion.seed` | Throwaway NKeys, gitignored. Regenerate with `nats auth nkey gen account/user` and update the two public keys in `server.conf`. |

## Run it

```bash
cd dev/secure-example

# 1. Keycloak (imports the realm; ~20s to ready)
docker run -d --name secure-example-keycloak -p 8903:8903 \
  -e KC_BOOTSTRAP_ADMIN_USERNAME=admin -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin \
  -e KC_HTTP_PORT=8903 \
  -v "$PWD/realm/secure-example-realm.json:/opt/keycloak/data/import/realm.json:ro" \
  quay.io/keycloak/keycloak:26.0 start-dev --import-realm

# 2. NATS
docker run -d --rm --name secure-example-nats -p 4233:4233 \
  -v "$PWD/server.conf:/server.conf:ro" nats:latest -c /server.conf -js

# 3. The callout service (build once: dotnet build -c Release src/AuthCallout.Host)
AUTHCALLOUT_BOOTSTRAP_PATH="$PWD/bootstrap.yaml" ASPNETCORE_URLS=http://localhost:8907 \
  ../../src/AuthCallout.Host/bin/Release/net10.0/AuthCallout.Host &

# 4. Prove it
dotnet run Prove.cs
```

Expected: `RESULT: 24 passed, 0 failed`.

> If you restart the NATS container, restart the host too: the KV identity
> store lives in NATS JetStream, and the host fails closed (denies
> authenticated grants) until its buckets are re-provisioned.

> The NATS server log shows `Subscription Violation - Subject "_INBOX.*"`
> for the .NET clients: expected and harmless. No profile grants an inbox
> subject, so the client's default request-reply inbox subscription is
> denied (request-reply inside a shared account needs the private-inbox pattern: custom `inbox_prefix` + a scoped `_INBOX.<x>.>` subscribe grant). Plain
> pub/sub, which is all this proof uses, is unaffected. Every negative
> assertion in `Prove.cs` is also corroborated by an explicit
> `Publish/Subscription Violation` in `docker logs secure-example-nats`,
> attributed to the right account/user (e.g. `ANON/user:anonymous`,
> `NOTES/user:alice`, `TASKS/user:carol`).

## What `Prove.cs` asserts

1. **Pre-authenticated users can authenticate**: two credential-less clients connect (the callout mints the anon grant).
2. **Anon no-cross-talk**: anon→anon delivery fails on an arbitrary subject *and* on the shared `notes.public.>` subject (requires the minter's empty-allow ⇒ `deny: [">"]` fix); anon cannot receive tenant events; the legitimate backend→anon public broadcast still arrives (via the account export).
3. **Authenticated member works**: alice (token claim `household_id: Fam42`) receives `notes.fam42.events.>` and her commands reach the backend.
4. **Tenant isolation**: alice can neither receive nor inject into bob's `fam99` subjects.
5. **End-to-end**: alice → backend → alice flows while bob observes nothing.
6. **Edge cases**: a garbage token is rejected at connect; bob's static `notes.public.>` grant works.
7. **Second app routes without collision**: carol (`azp: tasks-cli`, `team_id: Ops7`) gets `tasks.ops7.*` subjects in `TASKS`; her commands reach the tasks backend; other teams are denied.
8. **No cross-application leakage**: notes principals can't observe tasks traffic or vice versa (including each other's public feeds — the exports go to `ANON` only), and neither backend can inject into the other app's subjects.
9. **Anon union spans both apps (ADR 0004)**: anon receives `notes.public.>` *and* `tasks.public.>`, and can forge into neither.

All ports (4233 NATS, 8903 Keycloak, 8907 health) are non-default to avoid
colliding with the dev AppHost loops and local deploy stacks.

## Cleanup

```bash
docker rm -f secure-example-nats secure-example-keycloak
kill "$(cat .host.pid)" 2>/dev/null
```
