// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Host.Lifecycle;
using AuthCallout.Host.Wiring;
using AuthCallout.Secrets;

// Program.cs: DI composition, per doc 08 §Process composition. AuthCallout.Host is the
// integration point where every component packet's Contracts-seam implementation is wired
// together into the running service (M2 gate). Every step is delegated to a small,
// single-purpose type under Wiring/ so this entry point stays a short, readable sequence of
// named steps (see the packet file, implementation-plan/13-host-health-lifecycle.md, for the
// full task breakdown, and each Wiring/ type's own doc comment for what it does).

// 1. Bind + validate bootstrap config (doc 08 task 1). Fail-closed: exits before the host starts.
var bootstrap = await BootstrapLoading.LoadAndValidateAsync().ConfigureAwait(false);
if (bootstrap is null)
{
    return 1;
}

// 2. Resolve secrets (doc 08 task 1): issuer seed, optional service-user seed, optional xkey seed.
var secretResolver = new SecretResolver(onWarning: static msg => Console.Error.WriteLine($"WARN: {msg}"));
using var secretCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var secrets = await BootstrapSecrets.ResolveAsync(bootstrap, secretResolver, secretCts.Token).ConfigureAwait(false);

// 3. Select exactly one routing provider from source.kind (doc 08 task 1, ADR 0005).
var routingProvider = await RoutingProviderFactory.CreateAsync(bootstrap.Source, secretResolver, secretCts.Token).ConfigureAwait(false);

// 4. Lifecycle primitives + component graph + config-apply loop (doc 08 tasks 2-5).
var stateMachine = new ServiceStateMachine(bootstrap.Jwks.PrewarmRequired);
var activeConfigSource = new ActiveConfigSource();
var components = ComponentGraph.Build(bootstrap, secrets, activeConfigSource, stateMachine, secretResolver);
var configApplyLoop = ConfigApplyLoopFactory.Create(routingProvider, bootstrap, activeConfigSource, stateMachine, components);

// 5. Generic Host + minimal WebApplication for health on the pinned port (doc 08 task 1, D21).
await HealthAppBuilder.RunAsync(args, bootstrap, stateMachine, activeConfigSource, configApplyLoop, routingProvider, components).ConfigureAwait(false);
return 0;
