// -----------------------------------------------------------------------
// <copyright file="TestApplications.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NatsAuthCallout.Configuration;

namespace NatsAuthCallout.Tests;

/// <summary>
/// Shared application configurations used across unit tests. Mirrors a
/// household-scoped model (as an example domain) without any baked-in logic.
/// </summary>
internal static class TestApplications
{
    public const string ServiceNKey = "USERVICEACCOUNTNKEY";
    public const string Issuer = "https://issuer.example/realms/app";

    public static ApplicationOptions Household(string name = "app", string account = "APP")
    {
        return new ApplicationOptions
        {
            Name = name,
            TargetAccount = account,
            Routing = new RoutingOptions
            {
                ServiceAccountNKeys = [ServiceNKey],
                ConnectUsernames = [name],
                TokenIssuers = [Issuer],
            },
            Jwt = new JwtValidationOptions { Authority = Issuer },
            Permissions = new PermissionModelOptions
            {
                Anonymous = new GrantOptions
                {
                    Publish = [$"{name}.auth.commands", "_INBOX.>"],
                    Subscribe = ["_INBOX.>"],
                    ExpirySeconds = 600,
                },
                Authenticated = new AuthenticatedGrantOptions
                {
                    ScopeClaim = "household_id",
                    RequireScope = true,
                    NameClaim = "sub",
                    Subscribe = [$"{name}.events.>", "_INBOX.>"],
                    PublishPerScope =
                    [
                        $"{name}.house.{{scope}}.commands",
                        $"{name}.house.{{scope}}.queries",
                    ],
                    SubscribePerScope = [$"{name}.house.{{scope}}.events.>"],
                    FallbackExpirySeconds = 300,
                },
            },
        };
    }
}
