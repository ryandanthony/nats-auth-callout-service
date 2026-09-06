# 08 — Technical architecture (C# / .NET 10)

Concrete technology mapping for the rewrite. The language-agnostic design in [01](./01-overview.md)–[07](./07-kv-storage.md) is the contract; this document picks the .NET stack that implements it and pins the decisions that a language-neutral design leaves open. Library choices researched via Context7 against `/nats-io/nats.net` and `/kubernetes-client/csharp`.

**Target runtime:** .NET 10 (LTS). `net10.0` TFM, C# 14, nullable enabled, `Microsoft.NET.Sdk.Worker` host.

**Target NATS server:** 2.11+ (pinned). This is required for JetStream per-message TTL, which the identity stores rely on for per-record token-lifetime expiry (ADR 0010). Auth callout itself needs 2.10+, so 2.11+ covers both.

## Assumptions

- Single self-contained service process, one deployable container image (`ghcr.io/ryandanthony/nats-auth-callout-service`).
- Runs both in Kubernetes (CRD source) and standalone (files source); the process must not hard-depend on being in a cluster.
- Long-lived background worker, not a general HTTP/web app — no inbound business API beyond NATS (per [01](./01-overview.md) responsibilities). The only HTTP surface is a minimal health/readiness endpoint for the orchestrator (see [Health & readiness](#health--readiness)).
- Config-driven, fail-closed, hot-reloadable routing config.

## Stack selection

| Concern | Choice | Package | Why |
|---------|--------|---------|-----|
| Host | Generic Host worker | `Microsoft.Extensions.Hosting` | DI, config, logging, `IHostedService` lifecycle, graceful `SIGTERM` maps to the state machine in [05](./05-runtime-and-lifecycle.md). |
| NATS transport | **NATS.Net (v2)** | `NATS.Net` | Official, actively maintained v2 client. Native async (`IAsyncEnumerable`), JetStream + KV contexts, DI extension. Supersedes the legacy `NATS.Client` v1. |
| Callout JWT | **NATS.Jwt** | `NATS.Jwt` | Decode the authorization request claims and encode/sign the authorization response + minted user JWT. |
| NKeys / signing | **NATS.NKeys** | `NATS.NKeys` | Ed25519 seed handling for the issuer signing seed, service user seed, and assertion key. |
| Kubernetes source | **KubernetesClient** | `KubernetesClient` (`/kubernetes-client/csharp`) | Official .NET client. Generic custom-object + `Watch` API for cluster-wide CRD watch with label selector. |
| OIDC/JWKS validation | `Microsoft.IdentityModel.*` | `Microsoft.IdentityModel.Protocols.OpenIdConnect`, `System.IdentityModel.Tokens.Jwt` | Standard discovery via `.well-known/openid-configuration`, `ConfigurationManager<OpenIdConnectConfiguration>` gives JWKS fetch + cache + rotation for free (matches [04](./04-configuration.md) issuer model). |
| Config binding | Options + validation | `Microsoft.Extensions.Options`, `Microsoft.Extensions.Configuration` | Bootstrap layer binding, `IValidateOptions<T>` for fail-closed validation. |
| YAML files source | YamlDotNet | `YamlDotNet` | Per-app JSON/YAML files (JSON handled by `System.Text.Json`). |
| Secrets | custom `ISecretResolver` | (in-tree) | `inline | file | k8sSecret | vault` per [04](./04-configuration.md). File + k8sSecret first; vault behind the same interface. |
| Observability | OpenTelemetry | `OpenTelemetry.Extensions.Hosting`, `.Exporter.OtlpProtocol` | Metrics + traces + logs per [05 Observability](./05-runtime-and-lifecycle.md). One span per auth request, four stage events. |
| Health | Health checks + minimal API | `Microsoft.Extensions.Diagnostics.HealthChecks`, ASP.NET Core minimal APIs | Tagged component checks mapped to startup/live/ready probes with a JSON response writer (see [Health & readiness](#health--readiness)). |
| Resilience | Polly | `Microsoft.Extensions.Http.Resilience` / `Polly` | JWKS fetch retry/timeout, k8s watch reconnect backoff. |
| Tests | xUnit + Testcontainers | `xUnit`, `Testcontainers`, `NSubstitute` | Decision layer unit-tested in isolation; integration against real `nats-server -js` and a kind/k3s CRD via Testcontainers. |

### Why NATS.Net over the legacy client

`NATS.Net` (namespace `NATS.Net` / `NATS.Client.Core`) is the v2 rewrite and the one Context7's `/nats-io/nats.net` documents. It exposes exactly the contexts this service needs off a single connection:

```csharp
await using var nc = new NatsClient(); // or NatsConnection for fine control
var js  = nc.CreateJetStreamContext();
var kv  = nc.CreateKeyValueStoreContext();   // token / assertion / keys / discovery buckets
var svc = nc.CreateServicesContext();        // optional: service discovery/metrics
```

The legacy `NATS.Client` (v1) is in maintenance mode and lacks the async KV watch surface used in [07](./07-kv-storage.md). **Do not use it.**

> Caveat worth flagging in the rewrite: auth callout in .NET is assembled from `NATS.Net` (subscribe/reply on `$SYS.REQ.USER.AUTH`) + `NATS.Jwt` + `NATS.NKeys`. There is no single high-level "callout server" helper as of the current releases (`NATS.Jwt` 1.0.x, `NATS.NKeys` 1.0.x are recent/preview). Budget time for a thin callout adapter and pin package versions.

## Process composition

```mermaid
flowchart TD
  host[Generic Host] --> boot[BootstrapConfigLoader\nOptions + secrets]
  boot --> src{Source selector}
  src -->|files| filesrc[FileRoutingProvider\nIOptionsMonitor + FileSystemWatcher]
  src -->|k8s| k8ssrc[CrdRoutingProvider\nKubernetesClient Watch]
  filesrc --> agg[ConfigAggregator + Validator]
  k8ssrc --> agg
  agg --> active[(ActiveConfig snapshot\nvolatile swap)]
  host --> conn[NatsConnectionManager\nIHostedService]
  active --> conn
  conn --> listener[CalloutResponder\nSubscribeAsync $SYS.REQ.USER.AUTH]
  listener --> pipe[ResolutionPipeline\npure decision core]
  active --> pipe
  pipe --> validator[TokenValidator\nConfigurationManager + JWKS]
  pipe --> minter[UserJwtMinter\nNATS.Jwt + NKeys]
  minter --> listener
  pipe --> ident[IdentityStoreWriter\nKV: tokens/assertions/keys/discovery]
  pipe --> events[EventEmitter\nbest-effort publish]
  host --> otel[OpenTelemetry\nmetrics/traces/logs]
```

Only the k8s and files providers touch source-specific types; both map into the canonical model from [04](./04-configuration.md) before the pipeline sees them. `KubernetesClient` is referenced only by `CrdRoutingProvider`, keeping the cluster dependency out of the decision core (satisfies [ADR 0005](../architecture-decision-log/0005-config-abstraction-and-sources.md)).

## Project layout

```
src/
  AuthCallout.Host/            # Program.cs, DI wiring, Worker, health
  AuthCallout.Core/            # decision core: pipeline, selectors, matcher, minter (no I/O deps)
  AuthCallout.Contracts/       # canonical config model, Grant, ClaimMatch, SecretRef
  AuthCallout.Nats/            # NatsConnectionManager, CalloutResponder, IdentityStoreWriter, EventEmitter
  AuthCallout.Identity/        # TokenValidator, JwksProvider, AssertionSigner
  AuthCallout.Config.Files/    # FileRoutingProvider (+ YamlDotNet)
  AuthCallout.Config.K8s/      # CrdRoutingProvider (+ KubernetesClient) — isolated
  AuthCallout.Secrets/         # ISecretResolver + inline/file/k8s/vault impls
tests/
  AuthCallout.Core.Tests/      # pure, fast, no containers
  AuthCallout.Integration/     # Testcontainers: nats-server -js, k3s CRD
deploy/
  crd/                         # CustomResourceDefinition + RBAC (ClusterRole/Binding)
  helm/ or kustomize/
```

`AuthCallout.Core` and `AuthCallout.Contracts` reference no transport, storage, or k8s packages, so the whole authorization model is exercisable in isolation (matches the design's "Decision components contain no transport or storage code").

## Auth callout implementation shape

```csharp
// CalloutResponder — Serving state only
await foreach (var msg in nc.SubscribeAsync<byte[]>("$SYS.REQ.USER.AUTH", cancellationToken: ct))
{
    var request = NatsAuthorizationRequest.Decode(msg.Data);      // NATS.Jwt
    var decision = await pipeline.ResolveAsync(request, activeConfig.Current, ct);

    var responseJwt = decision switch
    {
        Grant g  => minter.MintUserJwt(g, request.UserNKey, issuerSeed),   // NATS.Jwt + NKeys
        Deny d   => minter.MintDenial(request, d.Reason),
    };

    await msg.ReplyAsync(responseJwt, cancellationToken: ct);      // signed authorization response
    _ = events.EmitAsync(decision);                               // best-effort, off critical path
}
```

The minter signs both the user JWT and the authorization response with the issuer signing seed (loaded once from a `SecretRef`, [05](./05-runtime-and-lifecycle.md)).

## Health & readiness

The service is not a business HTTP API, but it uses the **full** `Microsoft.Extensions.Diagnostics.HealthChecks` feature set behind a minimal `WebApplication` on a dedicated port (e.g. `8080`), separate from NATS traffic. Named component checks are tagged so the three Kubernetes probe types map to different check subsets, and a JSON response writer exposes per-check detail for humans and dashboards.

### Component checks

Each dependency is its own registered `IHealthCheck`, reporting `Healthy` / `Degraded` / `Unhealthy` with data:

| Check | Tags | Meaning |
|-------|------|---------|
| `self` | `live` | Process is up and the host is not faulted. |
| `nats-connection` | `ready` | NATS connection is open and the callout subscription to `$SYS.REQ.USER.AUTH` is active. |
| `config-active` | `ready` | A valid routing config is loaded (state is `Serving`, not `Idle`/`Starting`). |
| `config-source` | `ready` | The active source is healthy: file watcher alive, or the k8s CRD watch is connected (a dead watch is `Unhealthy`, [ADR 0005](../architecture-decision-log/0005-config-abstraction-and-sources.md)). |
| `jwks` | (none, informational) | Per-issuer JWKS reachable/cached; reported as `Degraded` on stale cache so it never fails readiness on a transient IdP blip. |
| `started` | `startup` | One-time: bootstrap loaded, secrets resolved, first config decision made. |

### Probe mapping

- **`/healthz` (liveness)** — `live`-tagged checks only. `200` while the process runs and isn't deadlocked; independent of config/NATS so a transient `Idle` never restarts the pod.
- **`/readyz` (readiness)** — `ready`-tagged checks. `200` only in the `Serving` state ([05](./05-runtime-and-lifecycle.md)): valid config active **and** NATS connected and subscribed. Any `Unhealthy` ready check returns `503` so the orchestrator stops routing to it.
- **`/startupz` (startup)** — `startup`-tagged. Gates the k8s `startupProbe` so slow first-config/secret resolution doesn't trip liveness during boot.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks()
    .AddCheck<SelfHealthCheck>("self", tags: ["live"])
    .AddCheck<NatsConnectionHealthCheck>("nats-connection", tags: ["ready"])
    .AddCheck<ConfigActiveHealthCheck>("config-active", tags: ["ready"])
    .AddCheck<ConfigSourceHealthCheck>("config-source", tags: ["ready"])
    .AddCheck<JwksHealthCheck>("jwks")                        // informational, untagged
    .AddCheck<StartupHealthCheck>("started", tags: ["startup"]);

var app = builder.Build();
var opts = new HealthCheckOptions { ResponseWriter = WriteJsonHealthResponse };  // per-check detail
app.MapHealthChecks("/healthz",  Filter("live",    opts));
app.MapHealthChecks("/readyz",   Filter("ready",   opts));
app.MapHealthChecks("/startupz", Filter("startup", opts));
// CalloutResponder, config providers, connection manager run as hosted services on the same host
app.Run();

static HealthCheckOptions Filter(string tag, HealthCheckOptions o) =>
    new() { Predicate = c => c.Tags.Contains(tag), ResponseWriter = o.ResponseWriter };
```

The component checks read the same state the `NatsConnectionManager`, config providers, and JWKS provider own, so health can't claim ready while the service answers nothing. The JSON writer emits each check's status, description, duration, and `data` (never secrets). Metrics still flow via the OTLP exporter; the health endpoints are for orchestrator probes and quick human inspection, not scraping.

## KV / identity stores (maps [07](./07-kv-storage.md) to NATS.Net KV)

```csharp
var kv = nc.CreateKeyValueStoreContext();
var tokens = await kv.CreateStoreAsync(new NatsKVConfig($"callout_apps_{slug}_oidc_tokens")
{
    // TTL matched to token lifetime per ADR 0010
});
await tokens.PutAsync(connectionId, oidcToken, ct);

// keys bucket watch for rotation / discovery consumers
await foreach (var e in keysStore.WatchAsync<string>(">", cancellationToken: ct))
{ /* react to rotation */ }
```

- Bucket names: KV bucket names cannot contain `.`, so the dotted subject paths in [07](./07-kv-storage.md)/[ADR 0010](../architecture-decision-log/0010-session-jwt-store-in-kv.md) become bucket name (`callout_apps_{slug}_oidc_tokens`) + key. Decide and pin the slug→bucket mapping once; it is an interop contract with consumers.
- Per-entry TTL: on the pinned 2.11+ server, use JetStream **per-message TTL** so each token record expires at its own token lifetime (ADR 0010), rather than a coarse bucket-wide `MaxAge`. The bucket may still set a `MaxAge` ceiling as a backstop.

## Kubernetes CRD source

```csharp
var config = KubernetesClientConfiguration.InClusterConfig();     // or BuildConfigFromConfigFile()
var client = new Kubernetes(config);

// cluster-wide watch, label selector claims owned CRs (ADR 0005)
var resp = client.CustomObjects.ListClusterCustomObjectWithHttpMessagesAsync(
    group: "authcallout.ants.zone", version: "v1", plural: "authapplications",
    labelSelector: ownerSelector, watch: true);

await foreach (var (type, item) in resp.WatchAsync<AuthApplication, object>(ct))
{
    // type: Added / Modified / Deleted -> re-aggregate + validate whole batch
}
```

- Watch must handle `410 Gone` (resource version expired) by relisting, and reconnect with backoff (Polly). A dropped watch that silently stops is a fail-open risk — treat watch loss as "config source unhealthy".
- RBAC: `ClusterRole` granting `list`/`watch`/`get` on the CRD cluster-wide, a `ServiceAccount`, and a `ClusterRoleBinding`. Ship in `deploy/crd/`.

## Config reload → in-memory swap

Active config is a single immutable snapshot behind a `volatile` reference (or `IOptionsMonitor`-style change token). Providers push a validated aggregate; `ConfigAggregator` validates the whole batch atomically ([05](./05-runtime-and-lifecycle.md) rules) and does one reference swap. In-flight requests keep their snapshot; new requests read the new one. No lock on the hot path.

## Container & supply chain

Shipped as a **multi-stage Dockerfile** (not `Microsoft.NET.Build.Containers`), so the build is explicit and portable across CI and local. The release flow matches the existing GHCR convention (GitVersion → `vX.Y.Z` tag → `docker/build-push-action` → GHCR → GitHub Release), adding **image signing**.

### Runtime & trimming

- **Framework-dependent, trimmed self-contained** on the `mcr.microsoft.com/dotnet/runtime` base (worker + minimal-API health only; the ASP.NET base is not required since there is no MVC/Razor). Use `linux-x64` (and `linux-arm64` if the NAS/cluster needs it).
- **Not full Native AOT.** NKeys/crypto and reflection-based config binding (`System.Text.Json` + options) make full AOT fragile; trimming with `TrimmerRootAssembly`/`DynamicDependency` for the config model is the pragmatic choice. Revisit AOT only if cold-start matters.
- Non-root user, read-only root filesystem, no shell in the final stage.

```dockerfile
# build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
ARG VERSION=0.0.0
RUN dotnet publish src/AuthCallout.Host -c Release -o /app \
    /p:Version=$VERSION /p:PublishTrimmed=true /p:InvariantGlobalization=true

# runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["./AuthCallout.Host"]
```

### Signing & provenance

- **Sign every published image with cosign (keyless, OIDC)** via the GitHub Actions identity: `cosign sign --yes ghcr.io/ryandanthony/nats-auth-callout-service@${DIGEST}`. No long-lived keys; signatures verifiable against the workflow identity. Requires `id-token: write` permission on the release job.
- **SBOM + attestation**: emit an SBOM (`docker buildx ... --sbom=true` or Syft) and attach it as a cosign attestation, alongside SLSA build provenance (`--provenance=true` on buildx). Deploys can then `cosign verify-attestation` before rollout.
- Sign the **digest**, not a mutable tag, so `latest`/version tags can move without invalidating the signature.

## What you're missing (gaps the language-neutral design leaves open)

These are not in [01](./01-overview.md)–[07](./07-kv-storage.md) and should be decided before or during the rewrite:

1. **Watch-loss = fail-closed semantics.** [05](./05-runtime-and-lifecycle.md) covers bad config, not a silently dead k8s watch or lost NATS connection. Define: lost NATS connection → stop answering (already implied); dead config watch → keep last-good but alert, with a staleness bound.
2. **Concurrency / backpressure.** Auth callout is on the client connect path. Define max in-flight auth requests, per-request timeout, and JWKS-fetch timeout so a slow IdP can't stall connects. Polly + bounded channel.
3. **Clock skew / token validation params.** `ClockSkew`, allowed algorithms (reject `none`/HS* when expecting RS/ES), `ValidateAudience` defaults per [04](./04-configuration.md). Make these explicit.
4. **Multi-instance behavior.** Cluster-wide CRD watch + label selector implies multiple instances can share a cluster. Confirm: are two instances answering the same callout account HA/active-active, and is that safe (idempotent KV writes, duplicate events)? Events stream is best-effort so duplicates are OK; confirm KV writes are last-writer-wins safe.
5. **Schema versioning for events + CRD.** Event `schemaVersion` exists ([05](./05-runtime-and-lifecycle.md)); the CRD needs its own `apiVersion` evolution story (v1 → v1beta conversions) and the discovery descriptor schema needs a version too.
6. **Local dev / no-k8s story.** Files source + a `docker-compose` with `nats-server -js` + Keycloak for a full loop, so the decision core is runnable without a cluster.

## Validation / feedback loop for the rewrite

- Unit: decision core (selectors, matcher, template variables, anonymous union) with table-driven claim fixtures — no containers, sub-second.
- Integration: Testcontainers boots `nats:2.11 -js`; assert a real client with a Keycloak-issued token connects and is granted the expected subjects, KV buckets populated, events observed.
- Contract: golden tests for canonical-model mapping from both files and CRD shapes so a source can't drift from the core.

## Sources

- NATS .NET: Context7 `/nats-io/nats.net` (KV `CreateStoreAsync`/`WatchAsync`, `NatsClient` contexts, Services API).
- Auth callout JWT/NKeys: `NATS.Jwt` 1.0.x, `NATS.NKeys` 1.0.x (NuGet); auth callout concept — <https://docs.nats.io/learn/security/auth-callout>.
- Kubernetes .NET: Context7 `/kubernetes-client/csharp` (`InClusterConfig`, custom objects, `Watch`).
