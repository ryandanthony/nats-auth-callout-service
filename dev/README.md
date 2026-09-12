# `dev/` — Aspire dev loops and sample services

Two [.NET Aspire](https://aspire.dev) AppHosts give a full local loop for
`nats-auth-callout-service` without hand-written compose files, and two sample
services (`NoteService`, `TaskService`) exercise both config sources and the
full identity-propagation sequence (doc 08 §Local development, doc 06).

The samples are built on [Wolverine](https://wolverinefx.net) over NATS: each
is an independent application with a small domain (commands, queries, and
events) backed by an in-memory store, and an in-process demo client that
drives traffic. Every client-originated command/query carries the connection's
signed `Callout-Identity` assertion, and a reusable Wolverine middleware
(`CalloutIdentityMiddleware`, in `Shared`) verifies it on the service side
before any handler runs — so the service authenticates every caller per
message, on top of the callout authenticating the NATS connection.

```
dev/
  AppHost.Local/   files source + external NATS + Keycloak (day-to-day inner loop)
  AppHost.K8s/      CRD source + local K3s cluster + NATS + Keycloak (cluster loop)
  samples/
    NoteService/    Wolverine app: Note domain (create/rename/get/list + events), notes.>
    TaskService/    second independent Wolverine app: TaskItem domain, tasks.>
    Shared/         SampleSupport: OIDC token fetch, callout connection/discovery,
                    identity middleware + verifier, assertion verify
```

**Subjects & grants.** Each app uses `{app}.commands` / `{app}.queries` /
`{app}.events` plus Wolverine's fixed `wolverine.response` reply subject; the
app YAML grant is widened to `[{app}.>, wolverine.response]` (the callout still
auto-adds the KV/JetStream/scoped-inbox plumbing). `wolverine.response` is
scoped inside each app's own NATS account, so app isolation (ADR 0001) holds:
a `notes` token never yields `tasks.>` and vice versa.

## Prerequisites

- .NET 10 SDK.
- Docker (both AppHosts run NATS, Keycloak, and — for `AppHost.K8s` — a K3s
  cluster as containers).
- A trusted local HTTPS dev certificate:
  ```
  dotnet dev-certs https --trust
  ```
  **Why this matters**: Aspire's `AddKeycloak` switches Keycloak's endpoint to
  HTTPS automatically in run mode once a dev cert is available. The callout's
  JWKS fetch path (`Microsoft.IdentityModel`'s `HttpDocumentRetriever`) has
  `RequireHttps` on unconditionally — including here in dev — because P06/P08
  never relax it for a "trusted" issuer. If Keycloak ends up serving plain
  HTTP (no cert trusted), JWKS prewarm fails and the callout never leaves
  `Starting` state. If port `8081` (`AppHost.Local`) or `8082`
  (`AppHost.K8s`) is already bound by something else on your machine, stop
  that first — Keycloak's container will still start, but the AppHost's own
  DCP tunnel proxy silently can't forward the host port and everything hangs
  in `Starting`.

## Running the loops

### AppHost.Local (files source, inner loop)

```
dotnet run --project dev/AppHost.Local
```

Starts, in order: NATS (JetStream, external port `4222`), Keycloak (dev
realm import from `AppHost.Local/realm/dev-realm.json`, host port `8081`),
`AuthCallout.Host` (files source, `Source__Kind` implied by
`config/bootstrap.yaml`, config directory `AppHost.Local/config/apps/`), then
`NoteService` and `TaskService`.

**What "working" looks like:**

- The Aspire dashboard (URL + one-time login token printed to the console)
  shows `nats`, `idp`, `callout`, `note`, `task` all `Running`.
- `curl http://localhost:8080/healthz` → `200 {"status":"Healthy",...}`
  (process is up — this one goes green almost immediately).
- `curl http://localhost:8080/readyz` → `200` once JWKS has prewarmed and the
  files config source has applied at least one batch (`config-active`,
  `config-source`, `nats-connection` checks). Until then it's `503` with
  `"state":"Starting"` — normal for the first 15-30s while Keycloak's realm
  import and the callout's JWKS prewarm both run.
- `curl http://localhost:8080/startupz` → `200` once the very first config
  decision has landed (D21's `/startupz` gate).
- The dashboard's **Traces** view shows auth spans from the callout as
  `note`/`task` connect and get authorized.
- `note`'s and `task`'s console logs (dashboard → resource → **Console
  logs**) show the Wolverine app starting, the demo client sending its
  commands/queries with a `Callout-Identity` header, and
  `CalloutIdentityMiddleware` logging `Verified caller identity for {message}:
  sub=note-user` (or `task-user`) before each handler runs — followed by the
  handler lines (`created note ...`, cascaded `event NoteCreated ...`, and the
  client's `ListNotes -> N note(s)` query result). A message that ever arrived
  without a valid assertion would log `Rejecting {message}: ...` and never
  reach a handler.

  The middleware's message-gating logic (reject without identity, run with a
  verified caller whose `sub` becomes the owner, query round-trip) is also
  covered directly against the compiled sample assemblies via Wolverine's
  in-memory transport, so it can be checked without NATS/Keycloak.

**Hot-reload check:** with the loop running, edit
`dev/AppHost.Local/config/apps/Note.yaml` (e.g. add a role to `match`) and
save. `FileRoutingConfigProvider`'s own file watcher (P10) picks it up
without restarting the callout — watch `config-source`'s
`lastSuccessfulSync` on `/readyz` advance, or the dashboard's structured
logs for a new config-apply batch.

**Why `nats/server.conf` exists.** `AuthCallout.Host`'s own service-user
connection runs in a dedicated `AUTH` account (doc 08 §Why the auth service
runs in its own account), so the NATS server needs `accounts {}` +
`auth_callout` wired by hand — Aspire's `AddNats` helper always injects a
global `--user`/`--pass`, which `nats-server` rejects once an `accounts {}`
block exists. See that file's own comments for two more things it encodes
that aren't obvious from the NATS docs alone: (1) `$JS.API.>`/`$KV.>` must be
exported from `AUTH` and imported into every target account, because the
identity-store KV buckets (discovery/keys/token/assertions) are created via
the callout's own `AUTH`-account connection but real clients connect into
`NOTES`/`TASKS`/`ANON` — NATS account isolation otherwise makes those
buckets invisible cross-account even though the minted grant's own
`$KV.<bucket>.<key>` subjects are correct; (2) the importing accounts must
**not** also set `jetstream: enabled` themselves, or their own local
JetStream domain silently shadows the `$JS.API.>` import. Both are recorded
as open M2 design questions for Ryan (should these grants move into
`GrantBuilder`/the bucket placement change?) — this file is the dev-loop
workaround in the meantime, not the intended long-term shape.

### AppHost.K8s (CRD source, cluster loop)

```
dotnet run --project dev/AppHost.K8s
```

Starts a single-node K3s cluster (`CommunityToolkit.Aspire.Hosting.K3s`,
persistent across AppHost restarts so you don't pay cluster bootstrap every
run), applies `notes`/`tasks` namespaces, the `AuthApplication` CRD +
RBAC from `deploy/crd/` (P11), and this AppHost's own
`dev/AppHost.K8s/manifests/samples/{Note,Task}.yaml` — **not**
`deploy/crd/samples/`, which models a production deployment (real issuer,
`owner: prod` label) rather than the local dev Keycloak. Then NATS
(external port `4223`, distinct from `AppHost.Local`'s `4222` so both loops
can run side by side), Keycloak (host port `8082`), `AuthCallout.Host`
(`Source__Kind=k8s`, `KUBECONFIG` injected automatically by
`WithReference(cluster)`), and the same two samples.

**What "working" looks like:** the same health-endpoint and dashboard checks
as `AppHost.Local`, plus the `k8s` cluster resource and the `namespaces`,
`crd`, `rbac`, `apps` manifest-apply resources all reaching `Finished`/
`Running` in the dashboard before `callout` starts.

**Known issue (2026-09-10, escalated, not dev/-fixable):** on a machine
with a `~/.kube/config` pointing at some *other* real cluster,
`AuthCallout.Config.K8s`'s `CrdKubernetesClient.ResolveConfig()` calls
`KubernetesClientConfiguration.BuildConfigFromConfigFile()` with no
arguments when not running in-cluster — that overload's null-path default
does **not** honor the `KUBECONFIG` environment variable Aspire injects; it
silently reads the fixed default `~/.kube/config` location instead. The
callout then tries to reach whatever cluster that file points at rather
than this loop's own K3s, and the k8s source never makes its first list
call — `/readyz` sits at `503`/`"state":"Starting"` forever with
`config-source.lastSuccessfulSync: never`. Verified everything *except*
that one call is correct on the K8s side: `kubectl --kubeconfig
dev/AppHost.K8s/.k3s/k8s/local/kubeconfig.yaml get authapplications -A`
shows both CRs applied with the right labels, and the identity that
kubeconfig grants can list/watch them. Unset/rename any conflicting
`~/.kube/config` as a workaround until the fix lands, or watch for a P11
commit swapping to `KubernetesClientConfiguration.BuildDefaultConfig()`
(which does check `KUBECONFIG`) or an explicit `Environment.GetEnvironmentVariable("KUBECONFIG")` pass-through.

**Live-patch check:** with the loop running and `kubectl` pointed at the
AppHost's cluster (`export KUBECONFIG=dev/AppHost.K8s/.k3s/k8s/local/
kubeconfig.yaml` from the AppHost directory once the cluster is up), patch
a CR, e.g.:
```
kubectl -n notes patch authapplication notes --type=merge \
  -p '{"spec":{"anonymousProfile":{"subscribe":["notes.public.>","notes.extra.>"]}}}'
```
`CrdRoutingConfigProvider`'s watch (P11) picks up the change and
re-aggregates — same `config-source` staleness signal on `/readyz` as the
files loop, and `kubectl get authapp notes -n notes -o
jsonpath='{.status}'` shows the status write-back (`observedGeneration`,
`accepted`).

## Minting a token by hand

Both samples fetch their own token via the Resource Owner Password grant
(`DevOidcTokenFetcher`, dev-only — a real client would use a browser
redirect flow). To do the same from a shell against either loop's Keycloak
(swap the port: `8081` for `AppHost.Local`, `8082` for `AppHost.K8s`):

```
curl -sk https://localhost:8081/realms/dev/protocol/openid-connect/token \
  -d grant_type=password \
  -d client_id=notes-web \
  -d username=note-user \
  -d password=note-pass \
  | jq -r .access_token
```

Dev realm users/roles (`dev/AppHost.Local/realm/dev-realm.json`, dev-only,
never a real credential):

| Username | Password | Realm role | Client (`azp`) |
|----------|----------|------------|-----------------|
| `note-user` | `note-pass` | `notes-editor` | `notes-web` |
| `task-user` | `task-pass` | `tasks-worker` | `tasks-web` |

## No real secrets

Every NKey seed in `AppHost.Local/config/bootstrap.yaml` and
`AppHost.K8s/config/bootstrap.yaml` is a throwaway generated for this dev
loop only — never used anywhere else, never rotated into a real deployment.
Regenerate them at any time with:

```
nats auth nkey gen account   # issuer/service-user seeds
nats auth nkey gen curve     # assertion key seed
```

Keycloak admin credentials are Aspire's own generated per-run parameters
(visible in the dashboard's resource details, never committed). The dev
realm's user passwords above are intentionally trivial and documented here
in plain text — they exist only inside a container Aspire tears down with
the AppHost.
