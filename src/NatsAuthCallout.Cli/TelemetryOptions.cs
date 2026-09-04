// -----------------------------------------------------------------------
// <copyright file="TelemetryOptions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace NatsAuthCallout.Cli;

/// <summary>
/// OpenTelemetry configuration bound from the <c>OpenTelemetry</c> section.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>
    /// Gets or sets the logical service name reported in telemetry resources.
    /// </summary>
    public string ServiceName { get; set; } = "nats-auth-callout";

    /// <summary>
    /// Gets or sets the OTLP exporter endpoint (e.g. <c>http://localhost:4317</c>).
    /// When empty, OTLP export is disabled.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OTLP protocol: <c>grpc</c> or <c>httpprotobuf</c>.
    /// </summary>
    public string Protocol { get; set; } = "grpc";

    /// <summary>
    /// Gets or sets a value indicating whether logs are written to the console
    /// (useful for local development and container logs).
    /// </summary>
    public bool Console { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether metrics and traces are also
    /// exported to the console. Verbose; intended for local debugging only.
    /// </summary>
    public bool ConsoleMetrics { get; set; }

    /// <summary>
    /// Gets or sets the minimum log level.
    /// </summary>
    public string LogLevel { get; set; } = "Information";
}
