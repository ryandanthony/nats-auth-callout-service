// -----------------------------------------------------------------------
// <copyright file="IntegrationCollection.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Integration.Fixtures;

/// <summary>
/// The single xUnit collection every scenario in this suite belongs to
/// (<c>[Collection(Name)]</c>), backed by <see cref="IntegrationEnvironment"/>. One NATS
/// container and one Keycloak container are started once and shared across every test class
/// (doc "Fixtures": "Collection fixtures share containers across tests; suite target &lt; ~10
/// min").
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationEnvironment>
{
#pragma warning disable S2339 // Must stay a compile-time constant: CollectionDefinition/Collection attributes require a const string argument, not a property (mirrors SecretResolver.DefaultK8sSecretMountRoot's own suppression for the same reason).
    /// <summary>Gets the collection name every scenario class references via <c>[Collection(IntegrationCollection.Name)]</c>.</summary>
    public const string Name = "AuthCallout.Integration";
#pragma warning restore S2339
}
