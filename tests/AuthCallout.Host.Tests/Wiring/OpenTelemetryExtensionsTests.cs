// -----------------------------------------------------------------------
// <copyright file="OpenTelemetryExtensionsTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Host.Wiring;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace AuthCallout.Host.Tests.Wiring;

/// <summary>
/// Covers the packet's ADR→test map row "Env-driven OTel config (D22)": resource
/// <c>service.name</c> follows <c>OTEL_SERVICE_NAME</c> when set, and falls back to
/// <see cref="OpenTelemetryExtensions.DefaultServiceName"/> when unset. Runs
/// <see cref="OpenTelemetryExtensions.AddAuthCalloutOpenTelemetry"/> through a real
/// <see cref="IServiceCollection"/>/<see cref="TracerProvider"/> build, since the behavior
/// under test is the OTel SDK resource-detection wiring, not a value this repo owns directly.
/// Tests mutate the process-wide <c>OTEL_SERVICE_NAME</c> environment variable and must not
/// run in parallel with each other (xUnit serializes tests within one class by default,
/// since there's no <c>[Collection]</c> opting out of that).
/// </summary>
public sealed class OpenTelemetryExtensionsTests
{
    private const string ServiceNameEnvVar = "OTEL_SERVICE_NAME";

    [Fact]
    public void Resource_service_name_follows_OTEL_SERVICE_NAME_when_set()
    {
        var previous = Environment.GetEnvironmentVariable(ServiceNameEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ServiceNameEnvVar, "my-custom-service-name");

            var serviceName = BuildAndReadServiceName();

            Assert.Equal("my-custom-service-name", serviceName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServiceNameEnvVar, previous);
        }
    }

    [Fact]
    public void Resource_service_name_falls_back_to_default_when_env_var_unset()
    {
        var previous = Environment.GetEnvironmentVariable(ServiceNameEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ServiceNameEnvVar, null);

            var serviceName = BuildAndReadServiceName();

            Assert.Equal(OpenTelemetryExtensions.DefaultServiceName, serviceName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServiceNameEnvVar, previous);
        }
    }

    private static string? BuildAndReadServiceName()
    {
        var services = new ServiceCollection();
        services.AddAuthCalloutOpenTelemetry();
        using var provider = services.BuildServiceProvider();
        using var tracerProvider = provider.GetRequiredService<TracerProvider>();

        var resource = tracerProvider.GetResource();
        return resource.Attributes.FirstOrDefault(static kvp => string.Equals(kvp.Key, "service.name", StringComparison.Ordinal)).Value as string;
    }
}
