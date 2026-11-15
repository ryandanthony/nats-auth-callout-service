// -----------------------------------------------------------------------
// <copyright file="ClientLibraryServiceConnectionTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using AuthCallout.Client.Connection;
using AuthCallout.Client.Tokens;
using AuthCallout.Contracts.Primitives;
using AuthCallout.Integration.Fixtures;
using AuthCallout.Integration.Support;
using NATS.Client.Core;

namespace AuthCallout.Integration.Scenarios;

/// <summary>
/// P19 task 6 (integration half), scenario 1: a service connects through
/// <c>AuthCallout.Client</c>'s <see cref="ClientCredentialsTokenProvider"/> against a
/// real Keycloak confidential client (<see cref="IntegrationRealm.ServiceClientId"/>, ADR 0002
/// <c>azp</c> match), and the resulting connection is scoped to exactly the matched profile's
/// grant — proven with a positive probe on the profile's own subject and a negative probe on a
/// subject the profile never grants. Reuses <see cref="HostFixture"/>/<see cref="TestApplications"/>
/// exactly as <see cref="HappyPathScenarioTests"/> does, driving the library instead of a
/// hand-rolled <c>NatsConnection</c>.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ClientLibraryServiceConnectionTests(IntegrationEnvironment environment) : IAsyncLifetime
{
    private HostFixture? _host;

    public async Task InitializeAsync()
    {
        var nats = environment.Nats;
        var keycloak = environment.Keycloak;
        _host = await HostFixture.StartAsync(
            nats,
            keycloak.CertificatePem,
            [TestApplications.NotesApp(keycloak.IssuerAuthority), TestApplications.TasksApp(keycloak.IssuerAuthority)],
            anonymous: null,
            CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    [Fact]
    public async Task ClientCredentialsTokenProvider_ConnectsAsService_GrantsExactlyTheServiceProfile()
    {
        var keycloak = environment.Keycloak;
        using var httpClient = TrustedHttpClientFactory.Create(keycloak.CertificatePem);
        var tokenEndpoint = new Uri($"{keycloak.BaseAddress}/realms/{IntegrationRealm.RealmName}/protocol/openid-connect/token");

        var providerOptions = new ClientCredentialsTokenProviderOptions
        {
            TokenEndpoint = tokenEndpoint,
            ClientId = IntegrationRealm.ServiceClientId,
            ClientSecret = SecretRef.Inline(IntegrationRealm.ServiceClientSecret),
        };
        var provider = new ClientCredentialsTokenProvider(providerOptions, httpClient);

        await using var connection = new CalloutNatsConnection(new CalloutClientOptions
        {
            Url = environment.Nats.ConnectionUrl,
            ApplicationName = TestApplications.TasksAppName,
            TokenProvider = provider,
            Name = "service-connect-test",
        });

        await connection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(CalloutConnectionState.Connected, connection.State);
        var client = connection.Connection!;

        // Positive: the worker-service profile's own grant (tasks.svc.>).
        await AssertPublishAllowedAsync(client, "tasks.svc.service-check");

        // Negative: a subject the service profile never grants.
        await AssertPublishDeniedAsync(client, "notes.dev.forbidden-for-service");

        provider.Dispose();
    }

    private static async Task AssertPublishAllowedAsync(NatsConnection client, string subject)
    {
        var violation = await ProbePublishAsync(client, subject);
        Assert.False(violation, $"Expected publish to '{subject}' to be allowed, but the server reported a permissions/authorization violation.");
    }

    private static async Task AssertPublishDeniedAsync(NatsConnection client, string subject)
    {
        var violation = await ProbePublishAsync(client, subject, deniedProbe: true);
        Assert.True(violation, $"Expected publish to '{subject}' to be denied, but no permissions/authorization violation was reported.");
    }

    /// <summary>Publishes a probe message and reports whether the server raised a permissions/authorization violation for it (mirrors <see cref="HappyPathScenarioTests"/>'s helper).</summary>
    private static async Task<bool> ProbePublishAsync(NatsConnection client, string subject, bool deniedProbe = false)
    {
        var violation = false;
        ValueTask OnError(object? sender, NatsServerErrorEventArgs args)
        {
            if (args.Kind is NatsServerErrorKind.PermissionsViolation or NatsServerErrorKind.AuthorizationViolation)
            {
                violation = true;
            }

            return ValueTask.CompletedTask;
        }

        client.ServerError += OnError;
        try
        {
            await client.PublishAsync(subject, Encoding.UTF8.GetBytes("probe"));
            await client.PingAsync();

            if (deniedProbe)
            {
                for (var attempt = 0; attempt < 20 && !violation; attempt++)
                {
                    await Task.Delay(100);
                    await client.PingAsync();
                }
            }
        }
        finally
        {
            client.ServerError -= OnError;
        }

        return violation;
    }
}
