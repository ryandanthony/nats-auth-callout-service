// -----------------------------------------------------------------------
// <copyright file="IntegrationEnvironment.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Integration.Fixtures;

/// <summary>
/// The shared collection fixture for <see cref="IntegrationCollection"/>: one
/// <see cref="NatsFixture"/> and one <see cref="KeycloakFixture"/>, started once for the
/// whole suite run and torn down once at the end. <see cref="Nats"/> is not xkey-configured
/// (<see cref="NatsFixture()"/>); the dedicated xkey scenario (P14 scenario 11) uses its own,
/// separately-started <see cref="NatsFixture"/> instance instead of this shared one, since
/// xkey is a server-config-time setting that would otherwise force every other scenario
/// through the encrypted path too.
/// </summary>
public sealed class IntegrationEnvironment : IAsyncLifetime
{
    /// <summary>Gets the shared NATS fixture (no xkey configured).</summary>
    /// <value>The shared NATS fixture.</value>
    public NatsFixture Nats { get; } = new();

    /// <summary>Gets the shared Keycloak fixture.</summary>
    /// <value>The shared Keycloak fixture.</value>
    public KeycloakFixture Keycloak { get; } = new();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await Task.WhenAll(Nats.InitializeAsync(), Keycloak.InitializeAsync());
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await Task.WhenAll(Nats.DisposeAsync(), Keycloak.DisposeAsync());
    }
}
