// -----------------------------------------------------------------------
// <copyright file="EventEmitter.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;
using NATS.Client.Core;

namespace AuthCallout.Nats.Events;

/// <summary>
/// <see cref="IEventEmitter"/> over a NATS connection (ADR 0008, doc 09 §1, P09). Builds the
/// v1 payload (<see cref="AuthOutcomePayloadBuilder"/>), resolves the target subject
/// (<see cref="EventSubjectResolver"/>) against the active config, and publishes
/// best-effort: a publish failure (or a missing/unset subject) is caught, never
/// propagated, and never delays the caller (ADR 0008: "must never delay or block the auth
/// response"). Every publish failure increments
/// <see cref="EventEmitterMetrics.PublishFailures"/> (<c>events_publish_failures_total</c>);
/// a silently-skipped event (no subject configured) does not, since that is expected,
/// non-error behavior.
/// </summary>
public sealed class EventEmitter : IEventEmitter
{
    private static readonly JsonSerializerOptions SerializerOptions = BuildSerializerOptions();

    private readonly INatsConnection _connection;
    private readonly IActiveConfigSource _activeConfigSource;
    private readonly TimeProvider _timeProvider;
    private readonly Action<Exception, string>? _onPublishFailure;

    /// <summary>Initializes a new instance of the <see cref="EventEmitter"/> class.</summary>
    /// <param name="connection">The NATS connection to publish on (the callout's own service-user connection, doc 05).</param>
    /// <param name="activeConfigSource">The current config snapshot, read once per emit (per-request consistency, doc 08 §Config reload).</param>
    /// <param name="timeProvider">The time source for the payload's <c>timestamp</c> field. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="onPublishFailure">
    /// Invoked with the swallowed exception and the subject that failed, after the failure is
    /// metered (ADR 0008: "logged/metered"). Optional; when null, only the metric is
    /// recorded. Never called with the payload or any secret material. Callers (P13/Host) may
    /// wire this to their logger for the "debug log" half of the packet's task 3.
    /// </param>
    public EventEmitter(
        INatsConnection connection,
        IActiveConfigSource activeConfigSource,
        TimeProvider? timeProvider = null,
        Action<Exception, string>? onPublishFailure = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(activeConfigSource);
        _connection = connection;
        _activeConfigSource = activeConfigSource;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _onPublishFailure = onPublishFailure;
    }

    /// <inheritdoc />
    public async Task EmitAsync(Decision decision, string? userNKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);

        // Best-effort per ADR 0008: no active config yet (Idle, doc 05) means no subjects to
        // resolve against, so there is nothing to publish. This is not a failure to meter —
        // it mirrors "no subject configured" (there IS no config-derived subject to try).
        var activeConfig = _activeConfigSource.Current;
        if (activeConfig is null)
        {
            return;
        }

        var subject = EventSubjectResolver.Resolve(decision, activeConfig.Routing, activeConfig.Bootstrap.Anonymous.Events);
        if (string.IsNullOrEmpty(subject))
        {
            return;
        }

        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey, _timeProvider.GetUtcNow());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);

        try
        {
            await _connection.PublishAsync(subject, bytes, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallow-and-count (ADR 0008 best-effort, P09 task 3): a publish failure must
            // never propagate into the caller (P07's critical path) or fail the auth
            // decision. OperationCanceledException is deliberately excluded: a caller-driven
            // cancellation is not a "publish failure" to meter, it is the caller no longer
            // wanting the emit attempt to continue.
            EventEmitterMetrics.PublishFailures.Add(1);
            _onPublishFailure?.Invoke(ex, subject);
        }
    }

    private static JsonSerializerOptions BuildSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new Rfc3339DateTimeOffsetConverter());
        return options;
    }
}
