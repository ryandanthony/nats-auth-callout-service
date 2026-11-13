// -----------------------------------------------------------------------
// <copyright file="BootstrapFileMapperJwksTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Host.Config;

namespace AuthCallout.Host.Tests.Config;

/// <summary>
/// Bootstrap <c>jwks</c> block mapping, focused on the optional dev/test-only
/// <c>jwks.allowInsecureHttp</c> flag (issue #2): defaults off, reads true/false, rejects
/// non-booleans, and unknown-key strictness still catches typos.
/// </summary>
public sealed class BootstrapFileMapperJwksTests
{
    [Fact]
    public void TryMap_AllowInsecureHttpOmitted_DefaultsToFalse()
    {
        var result = BootstrapFileMapper.TryMap(Bootstrap(jwksExtra: string.Empty));

        Assert.True(result.Succeeded, result.Error);
        Assert.False(result.Bootstrap!.Jwks.AllowInsecureHttp);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void TryMap_AllowInsecureHttpSet_IsRead(string json, bool expected)
    {
        var result = BootstrapFileMapper.TryMap(Bootstrap(jwksExtra: $", \"allowInsecureHttp\": {json}"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(expected, result.Bootstrap!.Jwks.AllowInsecureHttp);
    }

    [Fact]
    public void TryMap_AllowInsecureHttpNotBoolean_IsRejected()
    {
        var result = BootstrapFileMapper.TryMap(Bootstrap(jwksExtra: ", \"allowInsecureHttp\": \"yes\""));

        Assert.False(result.Succeeded);
        Assert.Contains("jwks.allowInsecureHttp", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryMap_AllowInsecureHttpTypo_IsRejectedAsUnknownKey()
    {
        var result = BootstrapFileMapper.TryMap(Bootstrap(jwksExtra: ", \"allowInsecureHtp\": true"));

        Assert.False(result.Succeeded);
        Assert.Contains("allowInsecureHtp", result.Error, StringComparison.Ordinal);
    }

    private static JsonElement Bootstrap(string jwksExtra)
    {
        var json = $$"""
        {
          "schemaVersion": 1,
          "natsUrl": "nats://nats:4222",
          "issuerSeed": { "kind": "inline", "value": "seed" },
          "source": { "kind": "files", "directory": "/config/apps", "warnStaleness": "60s", "maxStaleness": "15m", "reconnectBackoff": "5s" },
          "jwks": { "refreshInterval": "5m", "fetchTimeout": "5s", "prewarmRequired": true{{jwksExtra}} },
          "concurrency": { "maxDegreeOfParallelism": 64, "perRequestTimeout": "1500ms" },
          "anonymous": { "targetAccount": "ANON", "expiry": "5m" }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
