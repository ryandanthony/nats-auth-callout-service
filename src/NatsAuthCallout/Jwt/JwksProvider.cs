// -----------------------------------------------------------------------
// <copyright file="JwksProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.IdentityModel.Tokens;

namespace NatsAuthCallout.Jwt;

/// <summary>
/// Fetches and caches a JWKS (JSON Web Key Set) from an OIDC provider's certs
/// endpoint. Keys are refreshed after the cache TTL to support key rotation.
/// </summary>
public sealed class JwksProvider : IJwksProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _jwksUri;
    private readonly TimeSpan _cacheTtl;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private IList<SecurityKey>? _cachedKeys;
    private DateTimeOffset _validUntil;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwksProvider"/> class.
    /// </summary>
    /// <param name="jwksUri">The JWKS endpoint URI.</param>
    /// <param name="cacheTtl">How long fetched keys are cached.</param>
    /// <param name="timeProvider">The time provider (for testability).</param>
    /// <param name="httpClient">Optional shared HTTP client.</param>
    public JwksProvider(
        Uri jwksUri,
        TimeSpan cacheTtl,
        TimeProvider timeProvider,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(jwksUri);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _jwksUri = jwksUri;
        _cacheTtl = cacheTtl;
        _timeProvider = timeProvider;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <inheritdoc/>
    public async Task<IList<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedKeys is not null && _timeProvider.GetUtcNow() < _validUntil)
        {
            return _cachedKeys;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedKeys is not null && _timeProvider.GetUtcNow() < _validUntil)
            {
                return _cachedKeys;
            }

            var jwksJson = await _httpClient.GetStringAsync(_jwksUri, cancellationToken);
            var jwks = new JsonWebKeySet(jwksJson);
            _cachedKeys = jwks.GetSigningKeys();
            _validUntil = _timeProvider.GetUtcNow().Add(_cacheTtl);
            return _cachedKeys;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        _lock.Dispose();
    }
}
