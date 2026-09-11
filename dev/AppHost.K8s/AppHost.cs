// -----------------------------------------------------------------------
// <copyright file="AppHost.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

// AppHost.K8s (doc 08 §Local development, "AppHost 2"): the cluster loop. Same callout
// binary, samples, and NATS as AppHost.Local, but the callout runs against the Kubernetes
// CRD source (Source__Kind=k8s) backed by a real (lightweight) K3s cluster via the
// Community Toolkit's AddK3sCluster - no external cluster required. This is the only place
// the CRD path (AuthCallout.Config.K8s, P11) is exercised against a real API server outside
// Testcontainers-in-CI (P14); it proves the files and CRD sources converge on the identical
// canonical model (ADR 0007) end to end, not just in unit tests.

var builder = DistributedApplication.CreateBuilder(args);

// A single-node k3s cluster, persistent across AppHost restarts so re-running the loop
// doesn't pay the ~30-60s cluster bootstrap cost every time (doc 08: "no external cluster
// required"). WithReference(cluster) below injects KUBECONFIG automatically into the
// callout - host processes get the host-network kubeconfig variant, matching
// CrdKubernetesClient's dev fallback (BuildConfigFromConfigFile after
// KubernetesClientConfiguration.IsInCluster() is false, doc 08 §Kubernetes CRD source).
var cluster = builder.AddK3sCluster("k8s")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// CRD + RBAC come straight from P11's deploy/crd/ (the CRD and RBAC shape are shared
// with the production Helm chart, doc 09 §4). The sample CRs, though, are this AppHost's
// own dev/AppHost.K8s/manifests/samples/ rather than deploy/crd/samples/ directly:
// deploy/crd/samples/ models a *production* deployment (issuer https://id.ants.zone/...,
// owner label "prod" for P17's Helm-chart-driven ownership convention) - this loop needs
// the local dev Keycloak's issuer and an "owner: dev" label so P11's label-selector
// ownership (ADR 0005) only claims these dev CRs, never a stray prod-labelled CR someone
// applied while poking at the same cluster. WaitForCompletion(crd) orders the CR apply
// after the CRD's Established condition; the CRs land in the notes/tasks namespaces they
// declare in metadata.namespace, so those namespaces must exist first (kubectl apply -f,
// unlike -k, does not create parent namespaces from a CR alone).
var namespaces = cluster.AddK8sManifest("namespaces", "./manifests/namespaces.yaml");
var crd = cluster.AddK8sManifest("crd", "../../deploy/crd/authapplication-crd.yaml");
var rbac = cluster.AddK8sManifest("rbac", "../../deploy/crd/rbac.yaml")
    .WaitForCompletion(crd);
var apps = cluster.AddK8sManifest("apps", "./manifests/samples")
    .WaitForCompletion(crd)
    .WaitForCompletion(namespaces);

// NATS with JetStream, auth-callout-enabled (nats/server.conf - see AppHost.Local's copy of
// this file for why a plain AddContainer replaces Aspire's AddNats helper here: its built-in
// --user/--pass args conflict with the `accounts {}` block auth_callout's `account:` field
// requires). Pinned to host port 4223 (AppHost.Local uses 4222) so both dev loops can run
// side by side without a port clash. AuthCallout.Host resolves the NATS URL once from the
// static bootstrap file below rather than an Aspire-injected env var (doc 09 §Bootstrap
// file), so the port has to be fixed at a known value the bootstrap file can name, the same
// reasoning as AppHost.Local's own external NATS endpoint.
var nats = builder.AddContainer("nats", "nats", "2.14")
    .WithBindMount("./nats/server.conf", "/server.conf", isReadOnly: true)
    .WithArgs("-js", "-c", "/server.conf")
    .WithEndpoint("tcp", e =>
    {
        e.Port = 4223;
        e.TargetPort = 4222;
        e.IsExternal = true;
    });

// Keycloak dev IdP, same committed dev realm as AppHost.Local (clients/roles/users for
// both samples). AddKeycloak switches to https automatically in run mode when a dev HTTPS
// cert is available, which is what makes the issuer authority in each sample CR's
// spec.issuer.authority satisfy Microsoft.IdentityModel's JWKS fetch (see dev/README.md's
// "Keycloak must be served over HTTPS" note). Port 8082 here, distinct from
// AppHost.Local's 8081, so the two loops can run side by side without a host port clash.
var keycloak = builder.AddKeycloak("idp", 8082)
    .WithRealmImport("../AppHost.Local/realm");

var callout = builder.AddProject<Projects.AuthCallout_Host>("callout")
    .WaitFor(nats)
    .WithReference(cluster)
    .WaitForCompletion(rbac)
    .WaitForCompletion(apps)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithEnvironment("AUTHCALLOUT_BOOTSTRAP_PATH", Path.Combine(builder.AppHostDirectory, "config", "bootstrap.yaml"));

// NATS_URI: the container-network URL for the samples (DevEnvironment.ReadNatsUrl, dev/
// samples/Shared). nats isn't IResourceWithConnectionString (a plain AddContainer, see
// above), so WithReference(nats) wouldn't inject anything useful here - this builds the
// same nats://host:port shape by hand from its tcp endpoint, same as AppHost.Local.
builder.AddProject<Projects.NoteService>("note")
    .WithEnvironment("NATS_URI", ReferenceExpression.Create($"nats://{nats.GetEndpoint("tcp").Property(EndpointProperty.HostAndPort)}"))
    .WithReference(keycloak)
    .WaitFor(callout);

builder.AddProject<Projects.TaskService>("task")
    .WithEnvironment("NATS_URI", ReferenceExpression.Create($"nats://{nats.GetEndpoint("tcp").Property(EndpointProperty.HostAndPort)}"))
    .WithReference(keycloak)
    .WaitFor(callout);

await builder.Build().RunAsync();
