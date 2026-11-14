# secure-example — proof of the secure-by-default configuration

Runnable proof of `protecting-anon-access.md` §7: real `nats-server`, real
Keycloak, the real `AuthCallout.Host`, and a C# single-file proof app
(`Prove.cs`) that asserts 13 security properties by **actual message
delivery** (not CLI exit codes — an async permissions violation still looks
like a successful publish to the client).

## Layout

| File | Purpose |
| --- | --- |
| `server.conf` | Secure-by-default NATS config (doc §7.1): `AUTH` + empty `NOTES` + quarantined `ANON` with limits, `auth_callout`, no `no_auth_user`. One deliberate export: `notes.public.>` from `NOTES` into `ANON` so the public feed crosses the account wall. |
| `bootstrap.yaml` | NACS bootstrap (doc §7.2): anon → `ANON`, 5m expiry, files source, `allowInsecureHttp` for the local Keycloak. |
| `apps/Note.yaml` | The `notes` application (doc §7.3/§7.4): subscribe-only `anonymousProfile`, tenant-templated `member` profile, `azp`-matched `backend` profile. |
| `realm/secure-example-realm.json` | Keycloak realm: clients `notes-web` (public, password grant) and `notes-backend` (client credentials); users `alice` (household `Fam42`) and `bob` (`Fam99`) with the `notes-user` role and a `household_id` attribute mapper. |
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

Expected: `RESULT: 13 passed, 0 failed`.

> If you restart the NATS container, restart the host too: the KV identity
> store lives in NATS JetStream, and the host fails closed (denies
> authenticated grants) until its buckets are re-provisioned.

## What `Prove.cs` asserts

1. **Pre-authenticated users can authenticate**: two credential-less clients connect (the callout mints the anon grant).
2. **Anon no-cross-talk**: anon→anon delivery fails on an arbitrary subject *and* on the shared `notes.public.>` subject (requires the minter's empty-allow ⇒ `deny: [">"]` fix); anon cannot receive tenant events; the legitimate backend→anon public broadcast still arrives (via the account export).
3. **Authenticated member works**: alice (token claim `household_id: Fam42`) receives `notes.fam42.events.>` and her commands reach the backend.
4. **Tenant isolation**: alice can neither receive nor inject into bob's `fam99` subjects.
5. **End-to-end**: alice → backend → alice flows while bob observes nothing.
6. **Edge cases**: a garbage token is rejected at connect; bob's static `notes.public.>` grant works.

All ports (4233 NATS, 8903 Keycloak, 8907 health) are non-default to avoid
colliding with the dev AppHost loops and local deploy stacks.

## Cleanup

```bash
docker rm -f secure-example-nats secure-example-keycloak
kill "$(cat .host.pid)" 2>/dev/null
```
