// -----------------------------------------------------------------------
// <copyright file="OpenTelemetryExtensions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Host.Observability;
using AuthCallout.Nats.Events;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AuthCallout.Host.Wiring;

/// <summary>
/// Registers OpenTelemetry metrics and tracing (doc 05 §Observability, D22). The OTel SDK's
/// own <see cref="ResourceBuilder.CreateDefault"/> already reads <c>OTEL_SERVICE_NAME</c> and
/// <c>OTEL_RESOURCE_ATTRIBUTES</c> from the environment, and <c>AddOtlpExporter()</c> already
/// reads <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> (and the other <c>OTEL_EXPORTER_OTLP_*</c>
/// variables) — D22's "configurable at deployment time via env" is satisfied by the SDK
/// defaults, so this wiring only supplies <see cref="DefaultServiceName"/> as the fallback
/// when <c>OTEL_SERVICE_NAME</c> is unset, and lists every meter/activity source the service
/// (across every packet) registers.
/// </summary>
public static class OpenTelemetryExtensions
{
    /// <summary>Gets the fallback service name used when <c>OTEL_SERVICE_NAME</c> is unset (D22).</summary>
    /// <value>The fallback service name, <c>nats-auth-callout-service</c>.</value>
    public static string DefaultServiceName => "nats-auth-callout-service";

    /// <summary>Registers the OTel SDK: resource, meters (Host + component-owned), and the request-tracing activity source.</summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAuthCalloutOpenTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName: ResolveServiceName()))
            .WithMetrics(metrics => metrics
                .AddMeter(HostMetrics.MeterName)
                .AddMeter(EventEmitterMetrics.MeterName)
                .AddOtlpExporter())
            .WithTracing(tracing => tracing
                .AddSource(HostActivitySource.Name)
                .AddOtlpExporter());

        return services;
    }

    /// <summary>
    /// Resolves the effective service name: <c>OTEL_SERVICE_NAME</c> when set (the SDK
    /// resource detector would pick this up regardless; read explicitly here only so
    /// <see cref="DefaultServiceName"/> can be supplied as an explicit fallback via
    /// <c>AddService</c> rather than leaving the resource's <c>service.name</c> at the SDK's
    /// own generic default when the env var is absent).
    /// </summary>
    private static string ResolveServiceName()
    {
        var fromEnv = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME");
        return string.IsNullOrWhiteSpace(fromEnv) ? DefaultServiceName : fromEnv;
    }
}
