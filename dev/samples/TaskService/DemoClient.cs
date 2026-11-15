// -----------------------------------------------------------------------
// <copyright file="DemoClient.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace AuthCallout.Dev.Samples.TaskService;

/// <summary>
/// The <b>client</b> half of the tasks sample (see the notes sample's <c>DemoClient</c> for
/// the shared shape): drives a scripted workload over NATS through Wolverine. No identity
/// header attach here either — <c>AuthCallout.Client.Wolverine</c>'s
/// <c>CalloutIdentityOutboundRule</c> stamps it automatically, verified on receipt by the
/// package's <c>CalloutIdentityMiddleware</c>.
/// </summary>
public sealed class DemoClient : BackgroundService
{
    private readonly IMessageBus _bus;
    private readonly ILogger<DemoClient> _logger;

    /// <summary>Initializes a new instance of the <see cref="DemoClient"/> class.</summary>
    /// <param name="bus">The Wolverine message bus (sends over NATS).</param>
    /// <param name="logger">The client logger.</param>
    public DemoClient(IMessageBus bus, ILogger<DemoClient> logger)
    {
        ArgumentNullException.ThrowIfNull(bus);
        _bus = bus;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        var release = Guid.NewGuid();
        await _bus.SendAsync(new CreateTask(release, "ship the examples"));
        await _bus.SendAsync(new CreateTask(Guid.NewGuid(), "write the docs"));
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

        await _bus.SendAsync(new CompleteTask(release));
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

        var open = await _bus.InvokeAsync<IReadOnlyList<TaskItem>>(new GetOpenTasks(), stoppingToken);
        _logger.LogInformation("[tasks] client queried GetOpenTasks -> {Count} open: {Descriptions}", open.Count, string.Join(", ", open.Select(static t => t.Description)));

        _logger.LogInformation("[tasks] demo client finished.");
    }
}
