// -----------------------------------------------------------------------
// <copyright file="ConfigApplyLoopOptions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;

namespace AuthCallout.Host.Lifecycle;

/// <summary>
/// The staleness thresholds and optional callbacks <see cref="ConfigApplyLoop"/> needs,
/// grouped into one type so its constructor stays within the repo's parameter-count analyzer
/// limit (mirroring <c>AuthCallout.Nats.Callout.CalloutResponderDependencies</c>'s pattern) —
/// purely a parameter-list ergonomics grouping, not a new abstraction.
/// </summary>
/// <param name="WarnStaleness">ADR 0017 warn-staleness threshold.</param>
/// <param name="MaxStaleness">ADR 0017 max-staleness threshold.</param>
/// <param name="OnApplied">Invoked after every accepted apply (P08 bucket provisioning + discovery/keys publication, doc 08 task 3). Optional.</param>
/// <param name="OnRejected">Invoked with a deterministic reason string on every rejected batch (log + metric, ADR 0006). Optional.</param>
/// <param name="TimeProvider">Time source for the staleness clock. Defaults to <see cref="System.TimeProvider.System"/> when null.</param>
public sealed record ConfigApplyLoopOptions(
    TimeSpan WarnStaleness,
    TimeSpan MaxStaleness,
    Func<ActiveConfig, CancellationToken, Task>? OnApplied = null,
    Action<string>? OnRejected = null,
    TimeProvider? TimeProvider = null);
