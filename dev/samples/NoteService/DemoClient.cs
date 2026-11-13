// -----------------------------------------------------------------------
// <copyright file="DemoClient.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Dev.Samples.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace AuthCallout.Dev.Samples.NoteService;

/// <summary>
/// The <b>client</b> half of the notes sample, running as a hosted background service in the
/// same process as the service handlers. It drives a short scripted workload over NATS
/// through Wolverine — create a couple of notes, rename one, run a query — attaching this
/// connection's own signed <c>Callout-Identity</c> assertion as a Wolverine message header on
/// every send (<see cref="DeliveryOptions.WithHeader"/>, which Wolverine.Nats copies onto the
/// NATS message). Those sends leave over <c>notes.commands</c>/<c>notes.queries</c> and are
/// received by this same app's listeners, where <see cref="CalloutIdentityMiddleware"/>
/// verifies the assertion before any handler runs. Splitting the "client" into its own type
/// (rather than folding it into <c>Program</c>) keeps the send-side identity attachment in one
/// readable place that mirrors what a real, separate client application would do.
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
    public DemoClient(IMessageBus bus, NoteClientAssertion assertion, ILogger<DemoClient> logger)
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
        // Let the NATS listeners establish before the first send so nothing is missed.
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        var options = new DeliveryOptions().WithHeader(CalloutIdentityHeader.FallbackName, _assertion);

        var shopping = Guid.NewGuid();
        await _bus.SendAsync(new CreateNote(shopping, "Shopping", "milk, eggs"), options);
        await _bus.SendAsync(new CreateNote(Guid.NewGuid(), "Ideas", "write the examples doc"), options);
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

        await _bus.SendAsync(new RenameNote(shopping, "Groceries"), options);
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

        var all = await _bus.InvokeAsync<IReadOnlyList<Note>>(new ListNotes(), options, stoppingToken);
        _logger.LogInformation("[notes] client queried ListNotes -> {Count} note(s): {Titles}", all.Count, string.Join(", ", all.Select(static n => n.Title)));

        var one = await _bus.InvokeAsync<Note?>(new GetNote(shopping), options, stoppingToken);
        _logger.LogInformation("[notes] client queried GetNote({Id}) -> {Title}", shopping, one?.Title ?? "(not found)");

        _logger.LogInformation("[notes] demo client finished.");
    }
}

/// <summary>Wraps this connection's own signed assertion for DI injection into <see cref="DemoClient"/>.</summary>
/// <param name="CompactJwt">The compact assertion JWT this client attaches as its <c>Callout-Identity</c> header.</param>
public sealed record NoteClientAssertion(string CompactJwt);
