// -----------------------------------------------------------------------
// <copyright file="EventEmitterTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Nats.Events;
using NATS.Client.Core;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AuthCallout.Nats.Tests.Events;

/// <summary>
/// Unit tests for <see cref="EventEmitter"/> against a substituted <see cref="INatsConnection"/>
/// (P09 ADR→test map: "Publish failure swallowed + metered (ADR 0008 best-effort)" and the
/// overall never-throws/never-blocks contract). NSubstitute stands in for the unlanded
/// NATS connection dependency per the packet's DoD.
/// </summary>
public sealed class EventEmitterTests
{
    private static readonly DateTimeOffset FixedTimestamp = new(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EmitAsync_NoActiveConfigYet_DoesNotPublish_DoesNotThrow()
    {
        var connection = Substitute.For<INatsConnection>();
        var activeConfigSource = new FakeActiveConfigSource { Current = null };
        var emitter = new EventEmitter(connection, activeConfigSource, FixedTimeProvider());

        await emitter.EmitAsync(GrantedFor(null), userNKey: null, CancellationToken.None);

        await connection.DidNotReceiveWithAnyArgs().PublishAsync<byte[]>(default!, default!, cancellationToken: default);
    }

    [Fact]
    public async Task EmitAsync_NoSubjectConfiguredForOutcome_SkipsSilently_DoesNotPublish()
    {
        var connection = Substitute.For<INatsConnection>();
        var app = EventTestBuilders.ApplicationWithEvents("notes", events: null);
        var activeConfigSource = ActiveConfigSourceFor(app, anonymousEvents: null);
        var emitter = new EventEmitter(connection, activeConfigSource, FixedTimeProvider());

        await emitter.EmitAsync(GrantedFor("notes"), userNKey: null, CancellationToken.None);

        await connection.DidNotReceiveWithAnyArgs().PublishAsync<byte[]>(default!, default!, cancellationToken: default);
    }

    [Fact]
    public async Task EmitAsync_SubjectConfigured_PublishesToResolvedSubject_WithValidJsonPayload()
    {
        var connection = Substitute.For<INatsConnection>();
        var app = EventTestBuilders.ApplicationWithEvents("notes", new EventSubjects(Success: "notes.events.success", Failure: "notes.events.failure"));
        var activeConfigSource = ActiveConfigSourceFor(app, anonymousEvents: null);
        var emitter = new EventEmitter(connection, activeConfigSource, FixedTimeProvider());

        await emitter.EmitAsync(GrantedFor("notes"), userNKey: "UED123", CancellationToken.None);

        var call = connection.ReceivedCalls().Single();
        Assert.Equal("notes.events.success", call.GetArguments()[0]);
        var bytes = (byte[])call.GetArguments()[1]!;
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal("success", json.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("notes", json.RootElement.GetProperty("application").GetString());
        Assert.Equal("UED123", json.RootElement.GetProperty("userNKey").GetString());
    }

    [Fact]
    public async Task EmitAsync_PublishThrows_SwallowsException_DoesNotPropagate()
    {
        var connection = Substitute.For<INatsConnection>();
        connection
            .PublishAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<NatsHeaders>(), Arg.Any<string>(), Arg.Any<INatsSerialize<byte[]>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>())
            .ThrowsForAnyArgs(new NatsException("connection lost"));
        var app = EventTestBuilders.ApplicationWithEvents("notes", new EventSubjects(Success: "notes.events.success", Failure: null));
        var activeConfigSource = ActiveConfigSourceFor(app, anonymousEvents: null);
        var emitter = new EventEmitter(connection, activeConfigSource, FixedTimeProvider());

        // The core best-effort assertion: EmitAsync must not throw even though the
        // underlying publish did (ADR 0008: "must never delay or block the auth response").
        var exception = await Record.ExceptionAsync(() => emitter.EmitAsync(GrantedFor("notes"), userNKey: null, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task EmitAsync_PublishThrows_InvokesOnPublishFailureCallback_WithExceptionAndSubject()
    {
        var connection = Substitute.For<INatsConnection>();
        connection
            .PublishAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<NatsHeaders>(), Arg.Any<string>(), Arg.Any<INatsSerialize<byte[]>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>())
            .ThrowsForAnyArgs(new NatsException("connection lost"));
        var app = EventTestBuilders.ApplicationWithEvents("notes", new EventSubjects(Success: "notes.events.success", Failure: null));
        var activeConfigSource = ActiveConfigSourceFor(app, anonymousEvents: null);
        Exception? capturedException = null;
        string? capturedSubject = null;
        var emitter = new EventEmitter(
            connection,
            activeConfigSource,
            FixedTimeProvider(),
            onPublishFailure: (ex, subject) =>
            {
                capturedException = ex;
                capturedSubject = subject;
            });

        await emitter.EmitAsync(GrantedFor("notes"), userNKey: null, CancellationToken.None);

        Assert.NotNull(capturedException);
        Assert.IsType<NatsException>(capturedException);
        Assert.Equal("notes.events.success", capturedSubject);
    }

    [Fact]
    public async Task EmitAsync_PublishThrows_IncrementsPublishFailuresCounter()
    {
        var connection = Substitute.For<INatsConnection>();
        connection
            .PublishAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<NatsHeaders>(), Arg.Any<string>(), Arg.Any<INatsSerialize<byte[]>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>())
            .ThrowsForAnyArgs(new NatsException("connection lost"));
        var app = EventTestBuilders.ApplicationWithEvents("notes", new EventSubjects(Success: "notes.events.success", Failure: null));
        var activeConfigSource = ActiveConfigSourceFor(app, anonymousEvents: null);
        var emitter = new EventEmitter(connection, activeConfigSource, FixedTimeProvider());

        long total = 0;
        using (SubscribeToPublishFailuresCounter(measured => total += measured))
        {
            await emitter.EmitAsync(GrantedFor("notes"), userNKey: null, CancellationToken.None);
        }

        Assert.True(total > 0, $"Expected {EventEmitterMetrics.PublishFailuresCounterName} to record at least one measurement during the emit (recorded={total}).");
    }

    [Fact]
    public async Task EmitAsync_SuccessfulPublish_DoesNotInvokeOnPublishFailureCallback()
    {
        var connection = Substitute.For<INatsConnection>();
        var app = EventTestBuilders.ApplicationWithEvents("notes", new EventSubjects(Success: "notes.events.success", Failure: null));
        var activeConfigSource = ActiveConfigSourceFor(app, anonymousEvents: null);
        var callbackInvoked = false;
        var emitter = new EventEmitter(connection, activeConfigSource, FixedTimeProvider(), onPublishFailure: (_, _) => callbackInvoked = true);

        await emitter.EmitAsync(GrantedFor("notes"), userNKey: null, CancellationToken.None);

        Assert.False(callbackInvoked);
    }

    [Fact]
    public async Task EmitAsync_NullDecision_ThrowsArgumentNullException()
    {
        var connection = Substitute.For<INatsConnection>();
        var activeConfigSource = new FakeActiveConfigSource { Current = null };
        var emitter = new EventEmitter(connection, activeConfigSource, FixedTimeProvider());

        await Assert.ThrowsAsync<ArgumentNullException>(() => emitter.EmitAsync(null!, null, CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullConnection_ThrowsArgumentNullException()
    {
        var activeConfigSource = new FakeActiveConfigSource();

        Assert.Throws<ArgumentNullException>(() => new EventEmitter(null!, activeConfigSource));
    }

    [Fact]
    public void Constructor_NullActiveConfigSource_ThrowsArgumentNullException()
    {
        var connection = Substitute.For<INatsConnection>();

        Assert.Throws<ArgumentNullException>(() => new EventEmitter(connection, null!));
    }

    private static System.Diagnostics.Metrics.MeterListener SubscribeToPublishFailuresCounter(Action<long> onMeasured)
    {
        // System.Diagnostics.Metrics has no public "current value" read API by design (it is
        // an export-time aggregation model, not a gauge you poll); a MeterListener scoped to
        // the call under test is the documented way to observe a Counter<T>'s measurements
        // in-process. Since EventEmitterMetrics.Meter is a static, process-lifetime singleton
        // that was already created (and its counter already published) by an earlier test in
        // this run, this listener must also handle already-published instruments, not only
        // ones published after Start() — MeterListener.Start() itself replays
        // InstrumentPublished for every currently-live instrument, so this works either way.
        var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (string.Equals(instrument.Meter.Name, EventEmitterMetrics.MeterName, StringComparison.Ordinal)
                && string.Equals(instrument.Name, EventEmitterMetrics.PublishFailuresCounterName, StringComparison.Ordinal))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => onMeasured(measurement));
        listener.Start();
        return listener;
    }

    private static TimeProvider FixedTimeProvider() => new FixedTimeProviderImpl(FixedTimestamp);

    private static FakeActiveConfigSource ActiveConfigSourceFor(Application app, EventSubjects? anonymousEvents)
    {
        var bootstrap = EventTestBuilders.BootstrapWithAnonymousEvents(anonymousEvents);
        var routing = EventTestBuilders.RoutingFor(app);
        return new FakeActiveConfigSource { Current = EventTestBuilders.ActiveConfigFor(bootstrap, routing) };
    }

    private static GrantedDecision GrantedFor(string? application) => new(
        Application: application,
        Principal: application is null ? null : "user@example.com",
        Issuer: application is null ? null : "https://id.example.com",
        TargetAccount: "ACCT",
        Name: "user",
        Expiry: FixedTimestamp.AddMinutes(5),
        Publish: [],
        Subscribe: [],
        ProfileName: application is null ? null : "editor",
        IdentityPropagationEnabled: false,
        SelfLookupKey: null);

    private sealed class FixedTimeProviderImpl(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
