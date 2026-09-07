// -----------------------------------------------------------------------
// <copyright file="RoutingConfig.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Internal;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// The routing/apps aggregate (ADR 0005, ADR 0007): the hot-reloaded set of applications.
/// The single collection an <see cref="Application"/> instance is authored under; the
/// issuer→candidate-applications index and the app-slug map (doc 04 §Derived, not stored)
/// are computed here, never authored, so every provider (files/k8s/db) produces the same
/// aggregate shape and the core never re-derives them per request.
/// </summary>
/// <param name="Applications">The aggregated set of applications, one per configured unit.</param>
public sealed record RoutingConfig(IReadOnlyList<Application> Applications)
{
    private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<Application>>> _issuerIndex =
        new(() => BuildIssuerIndex(Applications));

    private readonly Lazy<IReadOnlyDictionary<string, Application>> _slugIndex =
        new(() => BuildSlugIndex(Applications));

    /// <summary>Gets an empty aggregate (no applications configured).</summary>
    /// <value>An empty routing aggregate.</value>
    public static RoutingConfig Empty { get; } = new([]);

    /// <summary>
    /// Gets the issuer-authority → candidate-applications index (doc 04 §Derived, not
    /// stored): every application configured with a given
    /// <see cref="IssuerConfig.Authority"/>, in aggregate order. Stage 1 of the resolution
    /// pipeline (doc 03) looks up a token's unvalidated <c>iss</c> here to find candidates
    /// and the shared JWKS authority.
    /// </summary>
    /// <value>The issuer-authority → candidate-applications index.</value>
    public IReadOnlyDictionary<string, IReadOnlyList<Application>> IssuerIndex => _issuerIndex.Value;

    /// <summary>
    /// Gets the application-slug → application index (doc 04 §Derived, not stored), keyed
    /// by each application's ADR 0013 slug. Built lazily; the aggregate validator
    /// guarantees slug uniqueness before this is ever consulted on the hot path.
    /// </summary>
    /// <value>The application-slug → application index.</value>
    public IReadOnlyDictionary<string, Application> SlugIndex => _slugIndex.Value;

    /// <inheritdoc />
    public bool Equals(RoutingConfig? other) =>
        other is not null && StructuralEquality.SequenceEqual(Applications, other.Applications);

    /// <inheritdoc />
    public override int GetHashCode() => StructuralEquality.SequenceHashCode(Applications);

    private static IReadOnlyDictionary<string, IReadOnlyList<Application>> BuildIssuerIndex(IReadOnlyList<Application> applications)
    {
        var index = new Dictionary<string, List<Application>>(StringComparer.Ordinal);
        foreach (var application in applications)
        {
            if (!index.TryGetValue(application.Issuer.Authority, out var candidates))
            {
                candidates = [];
                index[application.Issuer.Authority] = candidates;
            }

            candidates.Add(application);
        }

        return index.ToDictionary(
            static kvp => kvp.Key,
            IReadOnlyList<Application> (kvp) => kvp.Value,
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, Application> BuildSlugIndex(IReadOnlyList<Application> applications)
    {
        // Last-write-wins on a slug collision: the aggregate validator (which runs before
        // any RoutingConfig is accepted as active) rejects collisions outright, so this
        // index is only ever consulted against an already-validated aggregate.
        var index = new Dictionary<string, Application>(StringComparer.Ordinal);
        foreach (var application in applications)
        {
            index[application.Slug.Value] = application;
        }

        return index;
    }
}
