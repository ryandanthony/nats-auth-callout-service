// -----------------------------------------------------------------------
// <copyright file="CustomResourceMapResult.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;

namespace AuthCallout.Config.K8s.Mapping;

/// <summary>
/// The outcome of <see cref="CustomResourceSpecMapper.TryMap"/>: either a mapped
/// <see cref="Application"/>, or a deterministic error describing every problem found. Never
/// both set. The error text is surfaced verbatim in the CR's <c>status.reason</c> (ADR 0006,
/// doc 09 §4 §status subresource).
/// </summary>
/// <param name="Application">The mapped application, when mapping succeeded.</param>
/// <param name="Error">A deterministic, human-readable description of every problem found, when mapping failed.</param>
public sealed record CustomResourceMapResult(Application? Application, string? Error)
{
    /// <summary>Gets a value indicating whether mapping succeeded.</summary>
    /// <value>True when <see cref="Application"/> is set.</value>
    public bool IsSuccess => Application is not null;

    /// <summary>Creates a successful mapping result.</summary>
    /// <param name="application">The mapped application.</param>
    /// <returns>The constructed success result.</returns>
    public static CustomResourceMapResult Success(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return new CustomResourceMapResult(application, Error: null);
    }

    /// <summary>Creates a failed mapping result.</summary>
    /// <param name="error">The deterministic error description.</param>
    /// <returns>The constructed failure result.</returns>
    public static CustomResourceMapResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new CustomResourceMapResult(Application: null, error);
    }
}
