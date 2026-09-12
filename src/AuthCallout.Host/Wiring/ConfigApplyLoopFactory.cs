// -----------------------------------------------------------------------
// <copyright file="ConfigApplyLoopFactory.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;
using AuthCallout.Host.Lifecycle;

namespace AuthCallout.Host.Wiring;

/// <summary>
/// Builds the <see cref="ConfigApplyLoop"/> with its P08 bucket-provisioning/keys-publication
/// and rejection-logging callbacks wired (doc 08 task 3), factored out of
/// <c>Program.cs</c> purely to keep that entry point's own class-coupling count under the
/// repo's analyzer limit.
/// </summary>
public static class ConfigApplyLoopFactory
{
    /// <summary>Builds the config-apply loop for one running instance.</summary>
    /// <param name="routingProvider">The single active routing provider.</param>
    /// <param name="bootstrap">The validated bootstrap config.</param>
    /// <param name="activeConfigSource">The swap target for every accepted batch.</param>
    /// <param name="stateMachine">The state machine to report config-availability and freshness into.</param>
    /// <param name="components">The component graph, for the P08 bucket-provisioning callback and reload metrics.</param>
    /// <returns>The constructed config-apply loop.</returns>
    public static ConfigApplyLoop Create(
        IRoutingConfigProvider routingProvider,
        BootstrapConfig bootstrap,
        ActiveConfigSource activeConfigSource,
        ServiceStateMachine stateMachine,
        ComponentGraphResult components)
    {
        ArgumentNullException.ThrowIfNull(routingProvider);
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(activeConfigSource);
        ArgumentNullException.ThrowIfNull(stateMachine);
        ArgumentNullException.ThrowIfNull(components);

        var options = new ConfigApplyLoopOptions(
            bootstrap.Source.WarnStaleness.Value,
            bootstrap.Source.MaxStaleness.Value,
            OnApplied: (snapshot, ct) => OnAppliedAsync(components, snapshot, ct),
            OnRejected: static reason => Console.Error.WriteLine($"WARN: config batch rejected, keeping last-good (ADR 0006): {reason}"));

        return new ConfigApplyLoop(routingProvider, bootstrap, activeConfigSource, stateMachine, options);
    }

    private static async Task OnAppliedAsync(ComponentGraphResult components, ActiveConfig snapshot, CancellationToken cancellationToken)
    {
        components.Metrics.ConfigReloadsApplied.Add(1);
        await components.IdentityStoreWriter.ProvisionBucketsAsync(snapshot.Routing, cancellationToken);
        await components.IdentityStoreWriter.PublishKeysAndDiscoveryAsync(snapshot, cancellationToken);
    }
}
