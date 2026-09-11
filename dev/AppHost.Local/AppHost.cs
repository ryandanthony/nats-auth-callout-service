// -----------------------------------------------------------------------
// <copyright file="AppHost.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

// AppHost.Local (doc 08 §Local development, "AppHost 1"): files-source callout + external
// NATS + Keycloak dev IdP, this is the day-to-day inner loop. Editing config/apps/Note.yaml
// hot-reloads live (FileRoutingConfigProvider's own file watcher, P10; doc 08's inner loop).

var builder = DistributedApplication.CreateBuilder(args);

// NATS with JetStream, published to a host port so the `nats` CLI or scratch clients can
// also connect directly (doc 08: "connect to it externally and run locally" case).
var nats = builder.AddNats("nats")
    .WithJetStream()
    .WithDataVolume()
    .WithEndpoint("tcp", e =>
    {
        e.Port = 4222;
        e.TargetPort = 4222;
        e.IsExternal = true;
    });

// Keycloak dev IdP, importing the committed dev realm (clients/roles/users for both
// samples, dev-only credentials - see dev/README.md). 8081 on the host: 8080 is
// AuthCallout.Host's own pinned D21 health port, which the callout project binds directly
// via Kestrel (not through an Aspire-managed endpoint), so Keycloak must not claim it.
// Aspire's own AddKeycloak switches the endpoint to https automatically in run mode when a
// dev HTTPS cert is available (ASP.NET Core's dotnet dev-certs), which is what makes the
// issuer authority satisfy Microsoft.IdentityModel's HttpDocumentRetriever.RequireHttps
// (P06/P08's production JWKS fetch path never relaxes this - see dev/README.md's "Keycloak
// must be served over HTTPS" note for what breaks without it).
var keycloak = builder.AddKeycloak("idp", 8081)
    .WithRealmImport("./realm");

var callout = builder.AddProject<Projects.AuthCallout_Host>("callout")
    .WithReference(nats)
    .WaitFor(nats)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithEnvironment("AUTHCALLOUT_BOOTSTRAP_PATH", Path.Combine(builder.AppHostDirectory, "config", "bootstrap.yaml"));

builder.AddProject<Projects.NoteService>("note")
    .WithReference(nats)
    .WithReference(keycloak)
    .WaitFor(callout);

builder.AddProject<Projects.TaskService>("task")
    .WithReference(nats)
    .WithReference(keycloak)
    .WaitFor(callout);

await builder.Build().RunAsync();
