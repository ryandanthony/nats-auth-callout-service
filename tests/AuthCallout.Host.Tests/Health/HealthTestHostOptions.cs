// -----------------------------------------------------------------------
// <copyright file="HealthTestHostOptions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using AuthCallout.Host.Health;
using AuthCallout.Host.Lifecycle;
using NATS.Client.Core;
using NSubstitute;

namespace AuthCallout.Host.Tests.Health;

/// <summary>The collaborators <see cref="HealthTestHost.BuildAsync"/> wires; every property has a sensible default so a test only overrides what it's asserting on.</summary>
internal sealed class HealthTestHostOptions
{
    public ServiceStateMachine StateMachine { get; init; } = new(prewarmRequired: false);

    public INatsConnection Connection { get; init; } = SubstituteConnection(NatsConnectionState.Open);

    public IRoutingConfigProvider RoutingProvider { get; init; } = new StaticLastSyncRoutingProvider(DateTimeOffset.UtcNow);

    public IJwksCacheManager JwksCacheManager { get; init; } = Substitute.For<IJwksCacheManager>();

    public StartupHealthCheck StartupHealthCheck { get; init; } = new();

    public static INatsConnection SubstituteConnection(NatsConnectionState state)
    {
        var connection = Substitute.For<INatsConnection>();
        connection.ConnectionState.Returns(state);
        return connection;
    }
}
