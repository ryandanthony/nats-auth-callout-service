// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NatsAuthCallout;
using NatsAuthCallout.Cli;
using NatsAuthCallout.Configuration;

// ---------------------------------------------------------------------------
// Configuration: JSON files + environment variables + command-line args.
// Env var mapping example: Nats__IssuerSeed, Nats__Applications__0__Name, ...
// ---------------------------------------------------------------------------
var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var options = new AuthCalloutOptions();
configuration.GetSection("Nats").Bind(options);

// ---------------------------------------------------------------------------
// Telemetry: OpenTelemetry traces, metrics, and logs (OTLP + optional console).
// ---------------------------------------------------------------------------
var telemetryOptions = new TelemetryOptions();
configuration.GetSection("OpenTelemetry").Bind(telemetryOptions);

using var telemetry = Telemetry.Build(telemetryOptions);
using var loggerFactory = Telemetry.CreateLoggerFactory(telemetryOptions);
var logger = loggerFactory.CreateLogger<Program>();

try
{
    AuthCalloutFactory.Validate(options);
}
catch (InvalidOperationException ex)
{
    logger.LogCritical(ex, "Invalid configuration");
    return 1;
}

logger.LogInformation(
    "Starting NATS auth callout for {Count} application(s): {Apps}",
    options.Applications.Count,
    string.Join(", ", options.Applications.Select(a => a.Name)));

// ---------------------------------------------------------------------------
// Graceful shutdown on SIGINT / SIGTERM.
// ---------------------------------------------------------------------------
using var cts = new CancellationTokenSource();

void RequestShutdown()
{
    try
    {
        // ReSharper disable once AccessToDisposedClosure
        cts.Cancel();
    }
    catch (ObjectDisposedException)
    {
        // Already shutting down.
    }
}

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    RequestShutdown();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => RequestShutdown();

var selector = AuthCalloutFactory.CreateSelector(options, TimeProvider.System);

await using var runner = new AuthCalloutRunner(
    options, selector, loggerFactory.CreateLogger<AuthCalloutRunner>());

try
{
    await runner.StartAsync(cts.Token);
    await runner.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    logger.LogInformation("Shutdown requested.");
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Fatal error");
    return 1;
}

return 0;
