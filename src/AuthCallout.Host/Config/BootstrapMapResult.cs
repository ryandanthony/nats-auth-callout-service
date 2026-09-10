// -----------------------------------------------------------------------
// <copyright file="BootstrapMapResult.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;

namespace AuthCallout.Host.Config;

/// <summary>The outcome of <see cref="BootstrapFileMapper.TryMap"/>.</summary>
public sealed class BootstrapMapResult
{
    private BootstrapMapResult(BootstrapConfig? bootstrap, string? error)
    {
        Bootstrap = bootstrap;
        Error = error;
    }

    /// <summary>Gets the mapped bootstrap config, or null on failure.</summary>
    /// <value>The mapped bootstrap config, or null.</value>
    public BootstrapConfig? Bootstrap { get; }

    /// <summary>Gets the combined, deterministic error text, or null on success.</summary>
    /// <value>The combined error text, or null.</value>
    public string? Error { get; }

    /// <summary>Gets a value indicating whether mapping succeeded.</summary>
    /// <value>True when mapping succeeded.</value>
    public bool Succeeded => Bootstrap is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="bootstrap">The mapped bootstrap config.</param>
    /// <returns>The successful result.</returns>
    public static BootstrapMapResult Success(BootstrapConfig bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        return new BootstrapMapResult(bootstrap, error: null);
    }

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The combined, deterministic error text.</param>
    /// <returns>The failed result.</returns>
    public static BootstrapMapResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new BootstrapMapResult(bootstrap: null, error);
    }
}
