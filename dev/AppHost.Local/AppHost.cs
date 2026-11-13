// -----------------------------------------------------------------------
// <copyright file="AppHost.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

// AppHost.Local (doc 08 §Local development, "AppHost 1"): files-source callout + external
// NATS + Keycloak dev IdP, this is the day-to-day inner loop. Editing config/apps/Note.yaml
// hot-reloads live (FileRoutingConfigProvider's own file watcher, P10; doc 08's inner loop).
//
// `--callout local|docker` picks how the callout itself runs (everything else is identical):
//   local  (default for a bare `dotnet run` / IDE launch): AuthCallout.Host as an Aspire
//          project resource, i.e. the Debug/Release build of the working tree.
//   docker (what every `just` recipe passes): the repo's own Dockerfile built and run as a
//          container resource, i.e. the exact self-contained ReadyToRun image that ships, so
//          latency seen here is the shipped binary's, not the dev build's (doc 08 §Latency).
// Also settable as the `CALLOUT` environment variable / `callout` configuration key.

using AppHost.Local;

const string KeycloakName = "idp";
const int KeycloakHostPort = 8083;

var builder = DistributedApplication.CreateBuilder(args);

var calloutMode = (builder.Configuration["callout"] ?? "local").Trim();
var useDockerCallout = string.Equals(calloutMode, "docker", StringComparison.OrdinalIgnoreCase);
if (!useDockerCallout && !string.Equals(calloutMode, "local", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException($"Unknown --callout value '{calloutMode}': expected 'local' or 'docker'.");
}

// NATS with JetStream, auth-callout-enabled (nats/server.conf - see that file's own comment
// for why this can't use Aspire's AddNats helper: it always injects a global --user/--pass,
// which nats-server rejects once an `accounts {}` block exists, and auth_callout's own
// `account:` field requires exactly that). Published to a host port so the `nats` CLI or
// scratch clients can also connect directly (doc 08: "connect to it externally and run
// locally" case).
var nats = builder.AddContainer("nats", "nats", "2.14")
    .WithBindMount("./nats/server.conf", "/server.conf", isReadOnly: true)
    .WithArgs("-js", "-c", "/server.conf")
    .WithEndpoint("tcp", e =>
    {
        e.Port = 4222;
        e.TargetPort = 4222;
        e.IsExternal = true;
    });

// Keycloak dev IdP, importing the committed dev realm (clients/roles/users for both
// samples, dev-only credentials - see dev/README.md). 8083 on the host: 8080 is
// AuthCallout.Host's own pinned D21 health port (HostPorts.Health), used as its
// ConfigureKestrel fallback whenever ASPNETCORE_URLS isn't set (bare `dotnet run`,
// Docker/Kubernetes - see HealthAppBuilder). 8081 and 8082 can be taken by other local
// processes, so this loop uses 8083.
// Aspire's own AddKeycloak switches the endpoint to https automatically in run mode when a
// dev HTTPS cert is available (ASP.NET Core's dotnet dev-certs), which is what makes the
// issuer authority satisfy Microsoft.IdentityModel's HttpDocumentRetriever.RequireHttps
// (P06/P08's production JWKS fetch path never relaxes this - see dev/README.md's "Keycloak
// must be served over HTTPS" note for what breaks without it).
var keycloak = builder.AddKeycloak(KeycloakName, KeycloakHostPort)
    .WithRealmImport("./realm");

// Single callout instance for the local inner loop. Multi-replica horizontal scale-out was
// tried here (doc 08 §Multiple instances) but reverted: 3 callout replicas plus 3 load
// replicas drove enough continuous trace volume that the Aspire dashboard's in-memory trace
// buffer (fixed capacity, no persistence) rolled over within seconds, making it useless for
// inspecting any specific request. Scale-out itself already proved out (queue-group sharing
// confirmed, replica port collision fixed) — this is a dev-inner-loop trace-usability
// trade-off, not a regression. Bump WithReplicas(N) back temporarily for replica-specific
// investigation.
//
// Docker mode renders a container flavour of config/ into config-docker/ (git-ignored, kept
// in sync with config/apps/ while the AppHost runs, see DockerCalloutConfig) and mounts it
// at the image's default bootstrap path, /config. The container talks to NATS and Keycloak
// over Aspire's container network (the `nats` / `idp.dev.internal` aliases), trusts the
// ASP.NET Core dev cert Keycloak serves (WithDeveloperCertificateTrust), and publishes the
// pinned D21 health port 8080 so `curl localhost:8080/readyz` works the same in both modes.
// The image build restores FestinaLente.CodeStandards from GitHub Packages, so it needs the
// Dockerfile's `nuget_github_token` BuildKit secret: NUGET_GITHUB_TOKEN (the justfile fills
// it from `gh auth token`).
using var dockerConfig = useDockerCallout
    ? DockerCalloutConfig.Start(
        sourceDirectory: Path.Combine(builder.AppHostDirectory, "config"),
        outputDirectory: Path.Combine(builder.AppHostDirectory, "config-docker"),
        keycloakHostPort: KeycloakHostPort,
        keycloakResourceName: KeycloakName)
    : null;

IResourceBuilder<IResource> callout = dockerConfig is null
    ? builder.AddProject<Projects.AuthCallout_Host>("callout")
        .WaitFor(nats)
        .WithReference(keycloak)
        .WaitFor(keycloak)
        .WithEnvironment("AUTHCALLOUT_BOOTSTRAP_PATH", Path.Combine(builder.AppHostDirectory, "config", "bootstrap.yaml"))
    : builder.AddDockerfile("callout", contextPath: Path.Combine(builder.AppHostDirectory, "..", ".."))
        .WithBuildSecret("nuget_github_token", builder.AddParameterFromConfiguration("nuget-github-token", "NUGET_GITHUB_TOKEN", secret: true))
        .WithBindMount(dockerConfig.OutputDirectory, "/config", isReadOnly: true)
        .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "health")
        .WithHttpHealthCheck("/readyz", endpointName: "health")
        .WithOtlpExporter()
        .WithDeveloperCertificateTrust(trust: true)
        .WaitFor(nats)
        .WaitFor(keycloak);

// NATS_URI: the container-network URL for dependent resources (DevEnvironment.ReadNatsUrl,
// dev/samples/Shared). nats isn't IResourceWithConnectionString (a plain AddContainer, see
// above), so WithReference(nats) wouldn't inject anything useful here - this builds the same
// nats://host:port shape by hand from its tcp endpoint.
builder.AddProject<Projects.NoteService>("note")
    .WithEnvironment("NATS_URI", ReferenceExpression.Create($"nats://{nats.GetEndpoint("tcp").Property(EndpointProperty.HostAndPort)}"))
    .WithReference(keycloak)
    .WaitFor(callout);

builder.AddProject<Projects.TaskService>("task")
    .WithEnvironment("NATS_URI", ReferenceExpression.Create($"nats://{nats.GetEndpoint("tcp").Property(EndpointProperty.HostAndPort)}"))
    .WithReference(keycloak)
    .WaitFor(callout);

// LoadService (dev-only load generator): rapidly opens NATS connections, each of which drives
// one auth-callout request, so the callout_requests_* counters and the
// callout_request_duration_ms histogram move continuously and can be watched live in the
// dashboard's Metrics tab. Two axes of parallelism, so the callout's own bounded-concurrency
// path is actually exercised:
//   1. Each instance drives LOAD_CONCURRENCY parallel connect loops in-process.
//   2. Several independent processes run side by side as replicas of the single "load"
//      resource: raise LoadInstances to fan out wider.
// Replicas (rather than separate load-0, load-1, ... resources) give every process the same
// OTEL_SERVICE_NAME ("load") with a distinct service.instance.id, so the dashboard groups their
// telemetry (load_connect_duration_ms) as one app instead of N unrelated ones.
// LOAD_CONCURRENCY / LOAD_DELAY_MS / LOAD_DURATION_SEC apply to every replica.
// LoadInstances pinned to 1 (was 3): 3 load replicas x 3 callout replicas drove enough
// continuous trace volume that the Aspire dashboard's in-memory trace buffer (fixed
// capacity, no persistence) rolled over within seconds, making it useless for inspecting any
// specific request (same reasoning as callout's own single-instance comment above). Raise
// back temporarily for replica-fan-out-specific investigation.
const int LoadInstances = 1;
var natsUri = ReferenceExpression.Create($"nats://{nats.GetEndpoint("tcp").Property(EndpointProperty.HostAndPort)}");
builder.AddProject<Projects.LoadService>("load")
    .WithReplicas(LoadInstances)
    .WithEnvironment("NATS_URI", natsUri)
    .WithEnvironment("LOAD_CONCURRENCY", "8")
    .WithEnvironment("LOAD_DELAY_MS", "25")
    .WithReference(keycloak)
    .WaitFor(callout);

await builder.Build().RunAsync();
