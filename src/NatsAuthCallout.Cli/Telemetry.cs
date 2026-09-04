// -----------------------------------------------------------------------
// <copyright file="Telemetry.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace NatsAuthCallout.Cli;

/// <summary>
/// Wires OpenTelemetry metrics, tracing, and logging for the CLI. Exports via
/// OTLP when an endpoint is configured, and optionally to the console.
/// </summary>
public static class Telemetry
{
    /// <summary>
    /// Builds the metrics and tracing providers. Dispose to flush on shutdown.
    /// </summary>
    /// <param name="options">The telemetry options.</param>
    /// <returns>A disposable owning the meter and tracer providers.</returns>
    public static IDisposable Build(TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var resource = ResourceBuilder.CreateDefault().AddService(options.ServiceName);

        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter(AuthCalloutRunner.TelemetryName)
            .AddRuntimeInstrumentation()
            .ApplyExporters(options, isMetrics: true)
            .Build();

        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(AuthCalloutRunner.TelemetryName)
            .ApplyExporters(options, isMetrics: false)
            .Build();

        return new CompositeDisposable(meterProvider, tracerProvider);
    }

    /// <summary>
    /// Creates a logger factory that emits console logs and, when configured,
    /// OTLP logs.
    /// </summary>
    /// <param name="options">The telemetry options.</param>
    /// <returns>The logger factory.</returns>
    public static ILoggerFactory CreateLoggerFactory(TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var level = Enum.TryParse<LogLevel>(options.LogLevel, ignoreCase: true, out var parsed)
            ? parsed
            : LogLevel.Information;

        var resource = ResourceBuilder.CreateDefault().AddService(options.ServiceName);

        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(level);

            if (options.Console)
            {
                builder.AddSimpleConsole(c => c.SingleLine = true);
            }

            if (!string.IsNullOrWhiteSpace(options.Endpoint))
            {
                builder.AddOpenTelemetry(o =>
                {
                    o.SetResourceBuilder(resource);
                    o.IncludeScopes = true;
                    o.AddOtlpExporter(e => e.Configure(options));
                });
            }
        });
    }

    private static TBuilder ApplyExporters<TBuilder>(
        this TBuilder builder,
        TelemetryOptions options,
        bool isMetrics)
        where TBuilder : class
    {
        if (options.ConsoleMetrics)
        {
            if (isMetrics && builder is MeterProviderBuilder mb)
            {
                mb.AddConsoleExporter();
            }
            else if (!isMetrics && builder is TracerProviderBuilder tb)
            {
                tb.AddConsoleExporter();
            }
        }

        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            if (isMetrics && builder is MeterProviderBuilder mb)
            {
                mb.AddOtlpExporter((e, _) => e.Configure(options));
            }
            else if (!isMetrics && builder is TracerProviderBuilder tb)
            {
                tb.AddOtlpExporter(e => e.Configure(options));
            }
        }

        return builder;
    }

    private static void Configure(this OtlpExporterOptions exporter, TelemetryOptions options)
    {
        exporter.Endpoint = new Uri(options.Endpoint);
        exporter.Protocol = string.Equals(options.Protocol, "httpprotobuf", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;
    }

    private sealed class CompositeDisposable(params IDisposable[] disposables) : IDisposable
    {
        public void Dispose()
        {
            foreach (var disposable in disposables)
            {
                disposable.Dispose();
            }
        }
    }
}
