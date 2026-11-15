// -----------------------------------------------------------------------
// <copyright file="DemoClient.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace AuthCallout.Dev.Samples.NoteService;

/// <summary>
/// The <b>client</b> half of the notes sample, running as a hosted background service in the
/// same process as the service handlers. It drives a short scripted workload over NATS
/// through Wolverine — create a couple of notes, rename one, run a query. Unlike the pre-P20
/// sample, it attaches no identity header itself: <c>AuthCallout.Client.Wolverine</c>'s
/// <c>CalloutIdentityOutboundRule</c> stamps this connection's own signed
/// <c>Callout-Identity</c> assertion onto every outgoing envelope automatically. Those sends
/// leave over <c>notes.commands</c>/<c>notes.queries</c> and are received by this same app's
/// listeners, where the package's <c>CalloutIdentityMiddleware</c> verifies the assertion
/// before any handler runs. Splitting the "client" into its own type (rather than folding it
/// into <c>Program</c>) keeps the demo workload in one readable place that mirrors what a
/// real, separate client application would do.
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
        // Let the NATS listeners establish before the first send so nothing is missed.
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        var shopping = Guid.NewGuid();
        await _bus.SendAsync(new CreateNote(shopping, "Shopping", "milk, eggs"));
        await _bus.SendAsync(new CreateNote(Guid.NewGuid(), "Ideas", "write the examples doc"));
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

        await _bus.SendAsync(new RenameNote(shopping, "Groceries"));
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

        var all = await _bus.InvokeAsync<IReadOnlyList<Note>>(new ListNotes(), stoppingToken);
        _logger.LogInformation("[notes] client queried ListNotes -> {Count} note(s): {Titles}", all.Count, string.Join(", ", all.Select(static n => n.Title)));

        var one = await _bus.InvokeAsync<Note?>(new GetNote(shopping), stoppingToken);
        _logger.LogInformation("[notes] client queried GetNote({Id}) -> {Title}", shopping, one?.Title ?? "(not found)");

        _logger.LogInformation("[notes] demo client finished.");
    }
}
