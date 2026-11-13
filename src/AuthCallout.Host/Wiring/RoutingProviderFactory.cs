// -----------------------------------------------------------------------
// <copyright file="RoutingProviderFactory.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Config.Files;
using AuthCallout.Config.K8s;
using AuthCallout.Config.Postgres;
using AuthCallout.Config.Postgres.Repository;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Host.Wiring;

/// <summary>
/// Selects and constructs exactly one <see cref="IRoutingConfigProvider"/> from
/// <see cref="SourceConfig.Kind"/> (ADR 0005: "sources are not combined"; doc 08 §Process
/// composition "Source selector"). The Postgres connection string is resolved from its
/// <see cref="Primitives.SecretRef"/> here (startup-time-only, via <see cref="ISecretResolver"/>)
/// since <see cref="PostgresRoutingConfigProvider"/> itself takes a plain connection string.
/// </summary>
public static class RoutingProviderFactory
{
    /// <summary>Creates the single active routing provider for <paramref name="source"/>.</summary>
    /// <param name="source">The bootstrap source selection.</param>
    /// <param name="secretResolver">Resolves the Postgres connection string secret, when relevant.</param>
    /// <param name="cancellationToken">Cancellation for any secret resolution this factory performs.</param>
    /// <returns>The constructed provider.</returns>
    public static async Task<IRoutingConfigProvider> CreateAsync(SourceConfig source, ISecretResolver secretResolver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(secretResolver);

        switch (source.Kind)
        {
            case SourceKind.Files:
                return new FileRoutingConfigProvider(source.FilesSettings!);

            case SourceKind.K8s:
                return new CrdRoutingConfigProvider(source.K8sSettings!);

            case SourceKind.Postgres:
                return await CreatePostgresAsync(source.PostgresSettings!, secretResolver, cancellationToken);

            default:
                throw new InvalidOperationException($"Unrecognized SourceKind '{source.Kind}'.");
        }
    }

    private static async Task<IRoutingConfigProvider> CreatePostgresAsync(
        PostgresSourceSettings settings, ISecretResolver secretResolver, CancellationToken cancellationToken)
    {
        using var resolved = await secretResolver.ResolveAsync(settings.ConnectionString, cancellationToken);
        var repository = new PostgresAuthApplicationRepository(resolved.Reveal(), settings.NotifyChannel);
        return new PostgresRoutingConfigProvider(repository, settings.PollInterval.Value);
    }
}
