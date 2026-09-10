// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Validation;
using AuthCallout.Host.Config;
using AuthCallout.Host.Health;
using AuthCallout.Host.Lifecycle;
using AuthCallout.Host.Wiring;
using AuthCallout.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

// Program.cs: DI composition, per doc 08 §Process composition. AuthCallout.Host is the
// integration point where every component packet's Contracts-seam implementation is wired
// together into the running service (M2 gate). Construction is split across
// Wiring/BootstrapSecrets, Wiring/RoutingProviderFactory, and Wiring/ComponentGraph so this
// entry point stays a short, readable sequence of named steps (see the packet file,
// implementation-plan/13-host-health-lifecycle.md, for the full task breakdown).

// 1. Bind + validate bootstrap config (doc 08 task 1). Fail-closed: an unreadable or invalid
//    bootstrap file exits the process before the host even starts.
var bootstrapPath = BootstrapConfigLoader.ResolvePath();
var bootstrapMapResult = BootstrapConfigLoader.Load(bootstrapPath);
if (!bootstrapMapResult.Succeeded)
{
    await Console.Error.WriteLineAsync($"FATAL: bootstrap file '{bootstrapPath}' failed to load: {bootstrapMapResult.Error}").ConfigureAwait(false);
    return 1;
}

var bootstrapValidation = ConfigValidator.ValidateBootstrap(bootstrapMapResult.Bootstrap!);
if (!bootstrapValidation.IsValid)
{
    var reason = string.Join("; ", bootstrapValidation.Errors.Select(static e => $"{e.Code}: {e.Message}"));
    await Console.Error.WriteLineAsync($"FATAL: bootstrap file '{bootstrapPath}' is invalid: {reason}").ConfigureAwait(false);
    return 1;
}

var bootstrap = bootstrapMapResult.Bootstrap!;

// 2. Resolve secrets (doc 08 task 1): issuer seed, optional service-user seed, optional xkey seed.
var secretResolver = new SecretResolver(onWarning: static msg => Console.Error.WriteLine($"WARN: {msg}"));
using var secretCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var secrets = await BootstrapSecrets.ResolveAsync(bootstrap, secretResolver, secretCts.Token).ConfigureAwait(false);

// 3. Select exactly one routing provider from source.kind (doc 08 task 1, ADR 0005).
var routingProvider = await RoutingProviderFactory.CreateAsync(bootstrap.Source, secretResolver, secretCts.Token).ConfigureAwait(false);

// 4. Lifecycle primitives + component graph (doc 08 tasks 2-5).
var stateMachine = new ServiceStateMachine(bootstrap.Jwks.PrewarmRequired);
var activeConfigSource = new ActiveConfigSource();
var components = ComponentGraph.Build(bootstrap, secrets, activeConfigSource, stateMachine);

var configApplyLoop = new ConfigApplyLoop(
    routingProvider,
    bootstrap,
    activeConfigSource,
    stateMachine,
    bootstrap.Source.WarnStaleness.Value,
    bootstrap.Source.MaxStaleness.Value,
    onApplied: async (snapshot, ct) =>
    {
        components.Metrics.ConfigReloadsApplied.Add(1);
        await components.IdentityStoreWriter.ProvisionBucketsAsync(snapshot.Routing, ct).ConfigureAwait(false);
        await components.IdentityStoreWriter.PublishKeysAndDiscoveryAsync(snapshot, ct).ConfigureAwait(false);
    },
    onRejected: static reason =>
    {
        Console.Error.WriteLine($"WARN: config batch rejected, keeping last-good (ADR 0006): {reason}");
    });

// 5. Generic Host + minimal WebApplication for health on the pinned port (doc 08 task 1, D21).
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenAnyIP(HostPorts.Health));

builder.Services.AddAuthCalloutHost(bootstrap, stateMachine, activeConfigSource, configApplyLoop, routingProvider, components);

var app = builder.Build();
app.MapAuthCalloutHealthEndpoints();

await app.RunAsync().ConfigureAwait(false);
return 0;
