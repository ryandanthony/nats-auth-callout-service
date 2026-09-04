// -----------------------------------------------------------------------
// <copyright file="AuthCalloutFixture.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using NatsAuthCallout.Configuration;
using NATS.NKeys;
using Testcontainers.Nats;

namespace NatsAuthCallout.IntegrationTests;

/// <summary>
/// Boots a NATS container configured for auth callout and runs the callout
/// service in-process against it. Two applications are configured
/// (<c>app-a</c>, <c>app-b</c>) to prove per-application permission isolation.
/// Signing keys are generated at runtime, so no secrets are committed.
/// </summary>
public sealed class AuthCalloutFixture : IAsyncLifetime
{
    // AUTH account signing key (the auth_callout issuer).
    private readonly KeyPair _issuerAccountKey = KeyPair.CreatePair(PrefixByte.Account);

    // The callout service's own NATS user (member of the AUTH account).
    private readonly KeyPair _authUserKey = KeyPair.CreatePair(PrefixByte.User);

    // Service-account user for app-a (static APP-account user, bypasses callout).
    private readonly KeyPair _appAServiceKey = KeyPair.CreatePair(PrefixByte.User);

    private IContainer? _nats;
    private AuthCalloutRunner? _runner;
    private CancellationTokenSource? _runnerCts;
    private Task? _runnerTask;

    public TestJwtIssuer IssuerA { get; private set; } = null!;

    public TestJwtIssuer IssuerB { get; private set; } = null!;

    public string NatsUrl { get; private set; } = null!;

    public string AppAServiceUserSeed => _appAServiceKey.GetSeed();

    public string AppAServicePublicKey => _appAServiceKey.GetPublicKey();

    public async Task InitializeAsync()
    {
        IssuerA = new TestJwtIssuer("https://issuer.test/realms/app-a", "app-a-api");
        IssuerB = new TestJwtIssuer("https://issuer.test/realms/app-b", "app-b-api");
        IssuerA.Start();
        IssuerB.Start();

        var config = BuildNatsConfig();
        var configPath = Path.Combine(Path.GetTempPath(), $"auth-callout-{Guid.NewGuid():N}.conf");
        await File.WriteAllTextAsync(configPath, config);

        _nats = new NatsBuilder("nats:2-alpine")
            .WithResourceMapping(new FileInfo(configPath), new FileInfo("/etc/nats/auth-callout.conf"))
            .WithCommand("--config", "/etc/nats/auth-callout.conf")
            .WithPortBinding(4222, assignRandomHostPort: true)
            .Build();

        await _nats.StartAsync();
        NatsUrl = $"nats://127.0.0.1:{_nats.GetMappedPublicPort(4222)}";

        var options = BuildOptions();
        var selector = AuthCalloutFactory.CreateSelector(options, TimeProvider.System);
        _runner = new AuthCalloutRunner(options, selector, NullLogger<AuthCalloutRunner>.Instance);

        await _runner.StartAsync();
        _runnerCts = new CancellationTokenSource();
        _runnerTask = _runner.RunAsync(_runnerCts.Token);

        File.Delete(configPath);
    }

    private AuthCalloutOptions BuildOptions()
    {
        var appA = new ApplicationOptions
        {
            Name = "app-a",
            TargetAccount = "APP",
            Routing = new RoutingOptions
            {
                ServiceAccountNKeys = [AppAServicePublicKey],
                TokenIssuers = [IssuerA.Issuer],
            },
            Jwt = new JwtValidationOptions
            {
                Authority = IssuerA.Issuer,
                JwksUri = IssuerA.JwksUrl,
                Audience = IssuerA.Audience,
                ValidateAudience = true,
                JwksCacheMinutes = 5,
            },
            Permissions = HouseholdPermissions("app-a"),
        };

        var appB = new ApplicationOptions
        {
            Name = "app-b",
            TargetAccount = "APP",
            Routing = new RoutingOptions
            {
                TokenIssuers = [IssuerB.Issuer],
            },
            Jwt = new JwtValidationOptions
            {
                Authority = IssuerB.Issuer,
                JwksUri = IssuerB.JwksUrl,
                Audience = IssuerB.Audience,
                ValidateAudience = true,
                JwksCacheMinutes = 5,
            },
            Permissions = HouseholdPermissions("app-b"),
        };

        return new AuthCalloutOptions
        {
            Url = NatsUrl,
            IssuerSeed = _issuerAccountKey.GetSeed(),
            ServiceUserSeed = _authUserKey.GetSeed(),
            Applications = [appA, appB],
        };
    }

    private static PermissionModelOptions HouseholdPermissions(string prefix)
    {
        return new PermissionModelOptions
        {
            Anonymous = new GrantOptions
            {
                Publish = [$"{prefix}.auth.commands", "_INBOX.>"],
                Subscribe = ["_INBOX.>"],
                ExpirySeconds = 600,
            },
            Authenticated = new AuthenticatedGrantOptions
            {
                ScopeClaim = "household_id",
                RequireScope = true,
                NameClaim = "sub",
                Subscribe = [$"{prefix}.events.>", "_INBOX.>"],
                PublishPerScope =
                [
                    $"{prefix}.house.{{scope}}.commands",
                    $"{prefix}.house.{{scope}}.queries",
                ],
                SubscribePerScope = [$"{prefix}.house.{{scope}}.events.>"],
                FallbackExpirySeconds = 300,
            },
        };
    }

    private string BuildNatsConfig()
    {
        var sb = new StringBuilder();
        sb.AppendLine("port: 4222");
        sb.AppendLine("http_port: 8222");
        sb.AppendLine("jetstream { store_dir: /data }");
        sb.AppendLine("accounts {");
        sb.AppendLine("  AUTH: {");
        sb.AppendLine($"    users: [ {{ nkey: {_authUserKey.GetPublicKey()} }} ]");
        sb.AppendLine("  }");
        sb.AppendLine("  APP: {");
        sb.AppendLine("    jetstream: enabled");
        sb.AppendLine("    users: [");
        sb.AppendLine($"      {{ nkey: {_appAServiceKey.GetPublicKey()}, permissions: {{ publish: \">\", subscribe: \">\" }} }}");
        sb.AppendLine("    ]");
        sb.AppendLine("  }");
        sb.AppendLine("  SYS: {}");
        sb.AppendLine("}");
        sb.AppendLine("system_account: SYS");
        sb.AppendLine("authorization {");
        sb.AppendLine("  auth_callout {");
        sb.AppendLine($"    issuer: {_issuerAccountKey.GetPublicKey()}");
        sb.AppendLine("    auth_users: [");
        sb.AppendLine($"      {_authUserKey.GetPublicKey()},");
        sb.AppendLine($"      {_appAServiceKey.GetPublicKey()}");
        sb.AppendLine("    ]");
        sb.AppendLine("    account: AUTH");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    public async Task DisposeAsync()
    {
        if (_runnerCts is not null)
        {
            await _runnerCts.CancelAsync();
        }

        if (_runnerTask is not null)
        {
            try
            {
                await _runnerTask;
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        if (_runner is not null)
        {
            await _runner.DisposeAsync();
        }

        if (_nats is not null)
        {
            await _nats.DisposeAsync();
        }

        await IssuerA.DisposeAsync();
        await IssuerB.DisposeAsync();

        _issuerAccountKey.Dispose();
        _authUserKey.Dispose();
        _appAServiceKey.Dispose();
    }
}

/// <summary>xUnit collection so the fixture is shared across the test class.</summary>
[CollectionDefinition("AuthCallout")]
public sealed class AuthCalloutCollection : ICollectionFixture<AuthCalloutFixture>;
