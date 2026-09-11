// -----------------------------------------------------------------------
// <copyright file="FixtureSmokeTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Integration.Fixtures;
using NATS.Client.Core;

namespace AuthCallout.Integration.Scenarios;

/// <summary>
/// Sanity tests for <see cref="NatsFixture"/> and <see cref="KeycloakFixture"/> themselves —
/// the fixtures must be trustworthy (real containers actually start, actually answer) before
/// any scenario is built on top of them. Not a packet scenario in its own right; a foundation
/// check, mirroring <c>StubIdentityProviderTests</c>'s role for the P06 packet.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class FixtureSmokeTests(IntegrationEnvironment environment)
{
    [Fact]
    public async Task NatsFixture_ServiceUserSeed_ConnectsAndBypassesCallout()
    {
        // The service-user NKey is the auth_callout auth_users exemption (doc 08): it must
        // connect without any callout responder running at all.
        var nats = environment.Nats;
        await using var connection = new NatsConnection(new NatsOpts
        {
            Url = nats.ConnectionUrl,
            AuthOpts = new NatsAuthOpts { Seed = nats.ServiceUserKey.Seed, NKey = nats.ServiceUserKey.PublicKey },
        });

        await connection.ConnectAsync();

        Assert.Equal(NatsConnectionState.Open, connection.ConnectionState);
    }

    [Fact]
    public async Task KeycloakFixture_PasswordGrant_IssuesRealSignedToken()
    {
        var token = await environment.Keycloak.GetPasswordGrantTokenAsync(
            Support.IntegrationRealm.NotesClientId, Support.IntegrationRealm.NoteUsername, Support.IntegrationRealm.NotePassword);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(3, token.Split('.').Length); // header.payload.signature: a real JWS.
    }

    [Fact]
    public async Task KeycloakFixture_ClientCredentialsGrant_IssuesRealSignedToken()
    {
        var token = await environment.Keycloak.GetClientCredentialsTokenAsync(
            Support.IntegrationRealm.ServiceClientId, Support.IntegrationRealm.ServiceClientSecret);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(3, token.Split('.').Length);
    }
}
