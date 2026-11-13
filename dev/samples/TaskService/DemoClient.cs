// -----------------------------------------------------------------------
// <copyright file="DemoClient.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Dev.Samples.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace AuthCallout.Dev.Samples.TaskService;

/// <summary>
/// The <b>client</b> half of the tasks sample (see the notes sample's <c>DemoClient</c> for
/// the shared shape): drives a scripted workload over NATS through Wolverine, attaching this
/// connection's own signed <c>Callout-Identity</c> assertion on every send, which the tasks
/// service verifies via <see cref="CalloutIdentityMiddleware"/> before handling.
/// </summary>
public sealed class DemoClient : BackgroundService
{
    private readonly IMessageBus _bus;
    private readonly string _assertion;
    private readonly ILogger<DemoClient> _logger;

    /// <summary>Initializes a new instance of the <see cref="DemoClient"/> class.</summary>
    /// <param name="bus">The Wolverine message bus (sends over NATS).</param>
    /// <param name="assertion">This connection's own signed identity assertion (attached to every send).</param>
    /// <param name="logger">The client logger.</param>
    public DemoClient(IMessageBus bus, TaskClientAssertion assertion, ILogger<DemoClient> logger)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(assertion);
        _bus = bus;
        _assertion = assertion.CompactJwt;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        var options = new DeliveryOptions().WithHeader(CalloutIdentityHeader.FallbackName, _assertion);

        var release = Guid.NewGuid();
        await _bus.SendAsync(new CreateTask(release, "ship the examples"), options);
        await _bus.SendAsync(new CreateTask(Guid.NewGuid(), "write the docs"), options);
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

        await _bus.SendAsync(new CompleteTask(release), options);
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

        var open = await _bus.InvokeAsync<IReadOnlyList<TaskItem>>(new GetOpenTasks(), options, stoppingToken);
        _logger.LogInformation("[tasks] client queried GetOpenTasks -> {Count} open: {Descriptions}", open.Count, string.Join(", ", open.Select(static t => t.Description)));

        _logger.LogInformation("[tasks] demo client finished.");
    }
}

/// <summary>Wraps this connection's own signed assertion for DI injection into <see cref="DemoClient"/>.</summary>
/// <param name="CompactJwt">The compact assertion JWT this client attaches as its <c>Callout-Identity</c> header.</param>
public sealed record TaskClientAssertion(string CompactJwt);
