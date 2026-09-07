// -----------------------------------------------------------------------
// <copyright file="ValidationError.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Validation;

/// <summary>
/// One deterministic config-validation failure (doc 04 §Validation). The <see cref="Message"/>
/// text is stable across runs for the same input — these surface verbatim in the k8s CRD's
/// <c>status.reason</c> and in reject-batch logs (ADR 0006), so wording changes are a
/// contract change, not a free-form log line.
/// </summary>
/// <param name="Code">A short, stable machine-checkable identifier for this rule (e.g. <c>duplicate-application-name</c>), for tests and log filtering.</param>
/// <param name="Message">The human-readable, deterministic error text.</param>
public sealed record ValidationError(string Code, string Message)
{
    /// <inheritdoc />
    public override string ToString() => Message;
}
