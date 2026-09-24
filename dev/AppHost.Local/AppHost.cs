// -----------------------------------------------------------------------
// <copyright file="AppHost.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

// AppHost.Local (doc 08 §Local development, "AppHost 1"): files-source callout + external
// NATS + Keycloak dev IdP, this is the day-to-day inner loop. Editing config/apps/Note.yaml
// hot-reloads live (FileRoutingConfigProvider's own file watcher, P10; doc 08's inner loop).

var builder = DistributedApplication.CreateBuilder(args);

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
// AuthCallout.Host's own pinned D21 health port, which the callout project binds directly
// via Kestrel (not through an Aspire-managed endpoint), so Keycloak must not claim it; 8081
// and 8082 can be taken by other local processes, so this loop uses 8083.
// Aspire's own AddKeycloak switches the endpoint to https automatically in run mode when a
// dev HTTPS cert is available (ASP.NET Core's dotnet dev-certs), which is what makes the
// issuer authority satisfy Microsoft.IdentityModel's HttpDocumentRetriever.RequireHttps
// (P06/P08's production JWKS fetch path never relaxes this - see dev/README.md's "Keycloak
// must be served over HTTPS" note for what breaks without it).
var keycloak = builder.AddKeycloak("idp", 8083)
    .WithRealmImport("./realm");

var callout = builder.AddProject<Projects.AuthCallout_Host>("callout")
    .WaitFor(nats)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithEnvironment("AUTHCALLOUT_BOOTSTRAP_PATH", Path.Combine(builder.AppHostDirectory, "config", "bootstrap.yaml"));

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
const int LoadInstances = 3;
var natsUri = ReferenceExpression.Create($"nats://{nats.GetEndpoint("tcp").Property(EndpointProperty.HostAndPort)}");
builder.AddProject<Projects.LoadService>("load")
    .WithReplicas(LoadInstances)
    .WithEnvironment("NATS_URI", natsUri)
    .WithEnvironment("LOAD_CONCURRENCY", "8")
    .WithEnvironment("LOAD_DELAY_MS", "25")
    .WithReference(keycloak)
    .WaitFor(callout);

await builder.Build().RunAsync();
