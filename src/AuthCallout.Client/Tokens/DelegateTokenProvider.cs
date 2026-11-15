// -----------------------------------------------------------------------
// <copyright file="DelegateTokenProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Client.Tokens;

/// <summary>
/// An <see cref="ICalloutTokenProvider"/> that delegates both operations to caller-supplied
/// functions. For an app with its own auth stack (user login flow, token cache, etc.) that
/// wants to plug straight into the managed connection without adopting
/// <see cref="ClientCredentialsTokenProvider"/>.
/// </summary>
public sealed class DelegateTokenProvider : ICalloutTokenProvider
{
    private readonly Func<CancellationToken, ValueTask<string?>> _getToken;
    private readonly Func<CancellationToken, ValueTask>? _invalidate;

    /// <summary>Initializes a new instance of the <see cref="DelegateTokenProvider"/> class.</summary>
    /// <param name="getToken">Invoked by <see cref="GetTokenAsync"/>.</param>
    /// <param name="invalidate">Invoked by <see cref="InvalidateAsync"/>; omit if the caller's own token source has no invalidation concept (treated as a no-op).</param>
    public DelegateTokenProvider(Func<CancellationToken, ValueTask<string?>> getToken, Func<CancellationToken, ValueTask>? invalidate = null)
    {
        ArgumentNullException.ThrowIfNull(getToken);
        _getToken = getToken;
        _invalidate = invalidate;
    }

    /// <inheritdoc />
    public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken) => _getToken(cancellationToken);

    /// <inheritdoc />
    public ValueTask InvalidateAsync(CancellationToken cancellationToken) =>
        _invalidate?.Invoke(cancellationToken) ?? ValueTask.CompletedTask;
}
