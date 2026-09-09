// -----------------------------------------------------------------------
// <copyright file="ApplicationMapResult.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;

namespace AuthCallout.Config.Files.Parsing;

/// <summary>
/// The outcome of <see cref="ApplicationFileMapper.TryMap"/>: either a mapped
/// <see cref="Application"/>, or a deterministic error describing every problem found (doc 09
/// §3 rule table, ADR 0006). Never both set.
/// </summary>
/// <param name="Application">The mapped application, when mapping succeeded.</param>
/// <param name="Error">A deterministic, human-readable description of every problem found, when mapping failed.</param>
/// <param name="SchemaVersionDefaulted">
/// True when the file omitted <c>schemaVersion</c> and it was treated as <c>1</c> under the
/// doc 09 migration window. Reported regardless of overall success/failure so a caller can log
/// a warning even when other errors also caused the file to be rejected.
/// </param>
public sealed record ApplicationMapResult(Application? Application, string? Error, bool SchemaVersionDefaulted)
{
    /// <summary>Gets a value indicating whether mapping succeeded.</summary>
    /// <value>True when <see cref="Application"/> is set.</value>
    public bool IsSuccess => Application is not null;

    /// <summary>Creates a successful mapping result.</summary>
    /// <param name="application">The mapped application.</param>
    /// <param name="schemaVersionDefaulted">Whether <c>schemaVersion</c> was defaulted.</param>
    /// <returns>The constructed success result.</returns>
    public static ApplicationMapResult Success(Application application, bool schemaVersionDefaulted)
    {
        ArgumentNullException.ThrowIfNull(application);
        return new ApplicationMapResult(application, Error: null, schemaVersionDefaulted);
    }

    /// <summary>Creates a failed mapping result.</summary>
    /// <param name="error">The deterministic error description.</param>
    /// <param name="schemaVersionDefaulted">Whether <c>schemaVersion</c> was defaulted before the failure was found.</param>
    /// <returns>The constructed failure result.</returns>
    public static ApplicationMapResult Failure(string error, bool schemaVersionDefaulted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new ApplicationMapResult(Application: null, error, schemaVersionDefaulted);
    }
}
