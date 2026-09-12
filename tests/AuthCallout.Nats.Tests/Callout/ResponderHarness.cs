// -----------------------------------------------------------------------
// <copyright file="ResponderHarness.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;
using AuthCallout.Nats.Callout;
using NATS.Client.Core;
using NSubstitute;

namespace AuthCallout.Nats.Tests.Callout;

/// <summary>
/// Wires a <see cref="CalloutResponder"/> with substituted (or caller-supplied) collaborators
/// and drives it against synthetic <see cref="NatsMsg{T}"/> sequences, so
/// <see cref="CalloutResponderTests"/> can express each ADR→test map row as a short,
/// intention-revealing test body instead of repeating this wiring per test.
/// </summary>
internal sealed class ResponderHarness
{
    private readonly CalloutResponder _responder;
    private readonly INatsConnection _connection;
    private readonly List<byte[]> _replies = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _diagnostics = new();

    public ResponderHarness(ResponderHarnessOptions? harnessOptions = null)
    {
        var effective = harnessOptions ?? new ResponderHarnessOptions();
        var effectivePipeline = effective.Pipeline ?? DefaultPipeline();
        var effectiveActiveConfigSource = new FixedActiveConfigSource(effective.ActiveConfig ?? (effective.UseDefaultActiveConfig ? CalloutTestBuilders.ActiveConfigFor() : null));
        var effectiveMinter = effective.Minter ?? DefaultMinter();
        var effectiveCrypto = effective.Crypto ?? new NoOpCalloutCrypto();
        var effectiveIdentityStoreWriter = effective.IdentityStoreWriter ?? DefaultIdentityStoreWriter();
        var effectiveEventEmitter = effective.EventEmitter ?? DefaultEventEmitter();
        var effectiveOptions = effective.ResponderOptions ?? new CalloutResponderOptions(MaxDegreeOfParallelism: 4, PerRequestTimeout: TimeSpan.FromSeconds(5));

        var dependencies = new CalloutResponderDependencies(
            effectivePipeline, effectiveActiveConfigSource, effectiveMinter, effectiveCrypto, effectiveIdentityStoreWriter, effectiveEventEmitter);
        _responder = new CalloutResponder(dependencies, effectiveOptions, onDiagnostic: _diagnostics.Enqueue);

        _connection = effective.Connection ?? Substitute.For<INatsConnection>();
        if (effective.Connection is null)
        {
            WireCapture(_connection);
        }
    }

    public int ReplyCount => _replies.Count;

    public IReadOnlyCollection<string> Diagnostics => _diagnostics;

    /// <summary>Builds a minimal, valid <see cref="CalloutResponderDependencies"/> for constructor-validation tests that never actually run a request.</summary>
    public static CalloutResponderDependencies BuildDependencies() => new(
        DefaultPipeline(), new FixedActiveConfigSource(CalloutTestBuilders.ActiveConfigFor()), DefaultMinter(), new NoOpCalloutCrypto(), DefaultIdentityStoreWriter(), DefaultEventEmitter());

    public Task RunOneAsync(byte[] payload, string? xkeyHeaderValue = null) =>
        RunManyAsync([new SyntheticRequest(payload, xkeyHeaderValue)]);

    public Task RunManyAsync(IReadOnlyList<byte[]> payloads) =>
        RunManyAsync([.. payloads.Select(p => new SyntheticRequest(p, null))]);

    public async Task RunManyAsync(IReadOnlyList<SyntheticRequest> requests)
    {
        var messages = BuildMessages(requests);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _responder.RunAsync(messages, cts.Token);
    }

    /// <summary>Waits briefly for fire-and-forget event-emission tasks (never awaited by the responder itself) to complete, for tests that assert on them.</summary>
    public Task WaitForEventEmitAsync() => Task.Delay(TimeSpan.FromMilliseconds(100));

    public void AssertRepliedOnce(out byte[] replyBytes)
    {
        Assert.Single(_replies);
        replyBytes = _replies[0];
    }

    public void AssertNeverReplied() => Assert.Empty(_replies);

    private static IResolutionPipeline DefaultPipeline()
    {
        var pipeline = Substitute.For<IResolutionPipeline>();
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Decision>(CalloutTestBuilders.Granted()));
        return pipeline;
    }

    private static IUserJwtMinter DefaultMinter()
    {
        var minter = Substitute.For<IUserJwtMinter>();
        minter.MintGrant(Arg.Any<GrantedDecision>(), Arg.Any<string>(), Arg.Any<string>()).Returns(new SignedAuthResponse("default-grant-jwt"));
        minter.MintDenial(Arg.Any<DeniedDecision>(), Arg.Any<string>(), Arg.Any<string>()).Returns(new SignedAuthResponse("default-denial-jwt"));
        return minter;
    }

    private static IIdentityStoreWriter DefaultIdentityStoreWriter()
    {
        var writer = Substitute.For<IIdentityStoreWriter>();
        writer.WriteAsync(Arg.Any<GrantedDecision>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new IdentityStoreWriteResult(Succeeded: true, ConnectionId: "default-conn-id")));
        return writer;
    }

    private static IEventEmitter DefaultEventEmitter()
    {
        var emitter = Substitute.For<IEventEmitter>();
        emitter.EmitAsync(Arg.Any<Decision>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return emitter;
    }

    private void WireCapture(INatsConnection connection)
    {
#pragma warning disable VSTHRD110, MA0134 // NSubstitute's .When(x => x.AsyncMethod(...)) pattern intentionally invokes the async method without awaiting it: NSubstitute intercepts the call itself and never actually executes the real (ValueTask-returning) method body, so there is nothing to await.
        connection
            .When(c => c.PublishAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<NatsHeaders>(), Arg.Any<string>(), Arg.Any<INatsSerialize<byte[]>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>()))
            .Do(callInfo =>
            {
                lock (_replies)
                {
                    _replies.Add(callInfo.ArgAt<byte[]>(1));
                }
            });
#pragma warning restore VSTHRD110, MA0134
    }

    private async IAsyncEnumerable<NatsMsg<byte[]>> BuildMessages(IReadOnlyList<SyntheticRequest> requests)
    {
        foreach (var request in requests)
        {
            var headers = new NatsHeaders();
            if (request.XkeyHeaderValue is not null)
            {
                headers.Add(CalloutResponder.XkeyHeaderName, request.XkeyHeaderValue);
            }

            yield return new NatsMsg<byte[]>(CalloutResponder.Subject, "reply.inbox", request.Payload.Length, headers, request.Payload, _connection, NatsMsgFlags.None);
        }

        await Task.CompletedTask;
    }

    /// <summary>A fixed, never-changing <see cref="IActiveConfigSource"/> test double.</summary>
    private sealed class FixedActiveConfigSource(ActiveConfig? current) : IActiveConfigSource
    {
        public ActiveConfig? Current { get; } = current;

        /// <remarks>Never raised by this fixture: the responder only ever reads <see cref="Current"/> once per request.</remarks>
#pragma warning disable S108
        public event EventHandler<ActiveConfigChangedEventArgs>? Changed
        {
            add
            {
                // Intentionally unobserved by this fixture; see remarks.
            }

            remove
            {
                // Intentionally unobserved by this fixture; see remarks.
            }
        }
#pragma warning restore S108
    }
}
