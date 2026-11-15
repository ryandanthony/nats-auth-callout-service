// -----------------------------------------------------------------------
// <copyright file="StaticTokenProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Client.Tokens;

/// <summary>
/// An <see cref="ICalloutTokenProvider"/> that always returns the same, fixed token.
/// Intended for tests and CLI tools with a pre-minted, non-expiring (or short-lived test
/// run) token; <see cref="InvalidateAsync"/> is a no-op since there is nothing to re-mint.
/// </summary>
public sealed class StaticTokenProvider : ICalloutTokenProvider
{
    private readonly string? _token;

    /// <summary>Initializes a new instance of the <see cref="StaticTokenProvider"/> class.</summary>
    /// <param name="token">The fixed token to always return, or null to always report "no token available".</param>
    public StaticTokenProvider(string? token)
    {
        _token = token;
    }

    /// <inheritdoc />
    public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_token);

    /// <inheritdoc />
    public ValueTask InvalidateAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
