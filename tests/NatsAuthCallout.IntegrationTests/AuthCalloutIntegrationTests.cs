// -----------------------------------------------------------------------
// <copyright file="AuthCalloutIntegrationTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NATS.Client.Core;

namespace NatsAuthCallout.IntegrationTests;

/// <summary>
/// End-to-end tests exercising the callout against a real NATS server. Verifies
/// anonymous, authenticated, service-account, and cross-application isolation.
/// </summary>
[Collection("AuthCallout")]
[Trait("Category", "Integration")]
public sealed class AuthCalloutIntegrationTests(AuthCalloutFixture fixture)
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task Anonymous_CanPublish_AllowedSubject()
    {
        await using var conn = await ConnectAsync(token: null);

        // Publishing to an allowed subject must not tear down the connection.
        await conn.PublishAsync("app-a.auth.commands", "ping");
        await Task.Delay(300);

        Assert.True(await IsAliveAsync(conn));
    }

    [Fact]
    public async Task Anonymous_CannotRequest_HouseholdSubject()
    {
        await using var conn = await ConnectAsync(token: null);

        var allowed = await CanRequestAsync(conn, "app-a.house.house-1.commands");

        Assert.False(allowed);
    }

    [Fact]
    public async Task Authenticated_CanPublish_OwnHouseholdCommands()
    {
        var token = fixture.IssuerA.CreateToken(new Dictionary<string, object>
        {
            ["sub"] = "user-1",
            ["household_id"] = "house-1",
        });

        await using var conn = await ConnectAsync(token);

        await conn.PublishAsync("app-a.house.house-1.commands", "cmd");
        await Task.Delay(300);

        Assert.True(await IsAliveAsync(conn));
    }

    [Fact]
    public async Task Authenticated_CanSubscribe_OwnHouseholdEvents()
    {
        var token = fixture.IssuerA.CreateToken(new Dictionary<string, object>
        {
            ["sub"] = "user-1",
            ["household_id"] = "house-1",
        });

        await using var conn = await ConnectAsync(token);
        await using var sub = await conn.SubscribeCoreAsync<byte[]>("app-a.house.house-1.events.>");
        await Task.Delay(300);

        Assert.True(await IsAliveAsync(conn));
    }

    [Fact]
    public async Task Authenticated_CannotRequest_OtherHouseholdCommands()
    {
        var token = fixture.IssuerA.CreateToken(new Dictionary<string, object>
        {
            ["sub"] = "user-1",
            ["household_id"] = "house-1",
        });

        await using var conn = await ConnectAsync(token);

        var allowed = await CanRequestAsync(conn, "app-a.house.house-2.commands");

        Assert.False(allowed);
    }

    [Fact]
    public async Task InvalidToken_FallsBackToAnonymous()
    {
        await using var conn = await ConnectAsync("this.is.not-a-valid-jwt");

        // Anonymous subject allowed...
        await conn.PublishAsync("app-a.auth.commands", "ping");
        await Task.Delay(300);
        Assert.True(await IsAliveAsync(conn));

        // ...but household subject denied.
        Assert.False(await CanRequestAsync(conn, "app-a.house.house-1.commands"));
    }

    [Fact]
    public async Task MissingHouseholdClaim_FallsBackToAnonymous()
    {
        var token = fixture.IssuerA.CreateToken(new Dictionary<string, object>
        {
            ["sub"] = "user-1",
        });

        await using var conn = await ConnectAsync(token);

        Assert.False(await CanRequestAsync(conn, "app-a.house.house-1.commands"));
    }

    [Fact]
    public async Task ServiceAccount_GetsFullAccess()
    {
        // The APP-account static user bypasses the callout entirely and has ">".
        var seed = fixture.AppAServiceUserSeed;
        await using var conn = new NatsConnection(new NatsOpts
        {
            Url = fixture.NatsUrl,
            Name = "svc",
            AuthOpts = new NatsAuthOpts
            {
                AuthCredCallback = (_, _) => ValueTask.FromResult(NatsAuthCred.FromNkey(seed)),
            },
        });
        await conn.ConnectAsync();

        await conn.PublishAsync("anything.at.all", "x");
        await Task.Delay(300);

        Assert.True(await IsAliveAsync(conn));
    }

    // ----- Cross-application isolation ---------------------------------------

    [Fact]
    public async Task AppB_Token_CannotAccess_AppA_Subjects()
    {
        // A valid token from application B must not grant application A subjects.
        var tokenB = fixture.IssuerB.CreateToken(new Dictionary<string, object>
        {
            ["sub"] = "user-b",
            ["household_id"] = "house-1",
        });

        await using var conn = await ConnectAsync(tokenB);

        // app-b.house.house-1.commands is allowed; app-a.house.house-1.commands is not.
        Assert.False(await CanRequestAsync(conn, "app-a.house.house-1.commands"));
    }

    [Fact]
    public async Task AppB_Token_CanAccess_OwnHouseholdCommands()
    {
        var tokenB = fixture.IssuerB.CreateToken(new Dictionary<string, object>
        {
            ["sub"] = "user-b",
            ["household_id"] = "house-1",
        });

        await using var conn = await ConnectAsync(tokenB);

        await conn.PublishAsync("app-b.house.house-1.commands", "cmd");
        await Task.Delay(300);

        Assert.True(await IsAliveAsync(conn));
    }

    // ----- Helpers -----------------------------------------------------------

    private async Task<NatsConnection> ConnectAsync(string? token)
    {
        var opts = new NatsOpts { Url = fixture.NatsUrl, InboxPrefix = "_INBOX" };
        if (!string.IsNullOrEmpty(token))
        {
            opts = opts with { AuthOpts = new NatsAuthOpts { Token = token } };
        }

        var conn = new NatsConnection(opts);
        await conn.ConnectAsync();
        return conn;
    }

    private static async Task<bool> IsAliveAsync(NatsConnection conn)
    {
        try
        {
            await conn.PingAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> CanRequestAsync(NatsConnection conn, string subject)
    {
        using var cts = new CancellationTokenSource(ShortTimeout);
        try
        {
            await conn.RequestAsync<string, string>(subject, "x", cancellationToken: cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Timed out: no reply because the publish was denied (no listener either).
            return false;
        }
        catch (NatsException)
        {
            return false;
        }
    }
}
