// -----------------------------------------------------------------------
// <copyright file="WolverineHostFactory.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Tokens;
using AuthCallout.Client.Wolverine;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Nats;
using IHost = Microsoft.Extensions.Hosting.IHost;
using MicrosoftHost = Microsoft.Extensions.Hosting.Host;

namespace AuthCallout.Integration.Support;

/// <summary>
/// Builds the in-test <see cref="IHost"/> instances <c>Scenarios/Wolverine*.cs</c> drives: a
/// thin wrapper around <c>Host.CreateApplicationBuilder</c> + <c>UseWolverine</c> +
/// <c>UseNatsWithCallout</c> (ADR 0022 task 1), mirroring <c>dev/samples/NoteService/Program.cs</c>'s
/// shape but parameterized per scenario (application name is always
/// <see cref="TestApplications.WolverineAppName"/>; only the token provider, service name, and
/// subject wiring vary). <see cref="NatsTransportExtensions.UseNatsWithCallout"/> is the one
/// seam under test here — every host this factory builds exercises the real public API, never
/// a stand-in.
/// </summary>
public static class WolverineHostFactory
{
    /// <summary>
    /// Builds and starts a Wolverine host wired onto the callout, listening and publishing on
    /// <c>wolverine.dev.ping</c> (the <see cref="Scenarios.Ping"/>/<see cref="Scenarios.AnonymousPing"/>
    /// message types' subject). <c>DurabilityMode.Solo</c> avoids leader-election overhead/node
    /// collisions between the several hosts a single test process may start concurrently
    /// (each with its own <paramref name="serviceName"/>, since Wolverine's reply subject is
    /// per-<c>ServiceName</c>: <c>wolverine.response.{ServiceName}.&gt;</c>, and the
    /// <see cref="TestApplications.WolverineApp"/> grant already covers that whole subtree).
    /// </summary>
    /// <param name="natsUrl">The NATS fixture's connection URL.</param>
    /// <param name="serviceName">This host's unique Wolverine <c>ServiceName</c> (also used as the connection's display name).</param>
    /// <param name="tokenProvider">The token provider the transport's <c>AuthCallback</c> and outbound identity-header attach both use.</param>
    /// <param name="configureOptions">Optional extra <see cref="WolverineOptions"/> configuration (e.g. a different subject), applied after the subject wiring below.</param>
    /// <param name="cancellationToken">Cancellation for host startup.</param>
    /// <returns>The started host. Caller disposes it (<c>await host.StopAsync()</c> then <c>Dispose()</c>, or just <c>await using</c> if wrapped).</returns>
    public static async Task<IHost> StartAsync(
        string natsUrl,
        string serviceName,
        ICalloutTokenProvider tokenProvider,
        Action<WolverineOptions>? configureOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(natsUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        var builder = MicrosoftHost.CreateApplicationBuilder();

        builder.UseWolverine(opts =>
        {
            opts.ServiceName = serviceName;
            opts.Durability.Mode = DurabilityMode.Solo;

            // GH-3521: the application assembly for handler discovery is a process-wide value
            // pinned by whichever Wolverine host starts first in this test process. Every
            // scenario's handlers (PingHandlers) live in this same test assembly, so pin it
            // explicitly rather than relying on caller-assembly inference, which would otherwise
            // vary depending on which host (client or service) happens to start first.
            opts.ApplicationAssembly = typeof(Scenarios.PingHandlers).Assembly;

            opts.UseNatsWithCallout(callout =>
            {
                callout.Url = natsUrl;
                callout.ApplicationName = TestApplications.WolverineAppName;
                callout.TokenProvider = tokenProvider;
                callout.Name = serviceName;
            });

            opts.PublishMessage<Scenarios.Ping>().ToNatsSubject("wolverine.dev.ping");
            opts.PublishMessage<Scenarios.Pong>().ToNatsSubject("wolverine.dev.pong");
            opts.PublishMessage<Scenarios.AnonymousPing>().ToNatsSubject("wolverine.dev.anonymous-ping");

            opts.ListenToNatsSubject("wolverine.dev.ping");
            opts.ListenToNatsSubject("wolverine.dev.pong");
            opts.ListenToNatsSubject("wolverine.dev.anonymous-ping");

            configureOptions?.Invoke(opts);
        });

        var host = builder.Build();
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }
}
