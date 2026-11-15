// -----------------------------------------------------------------------
// <copyright file="ClientCredentialsTokenProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AuthCallout.Contracts.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuthCallout.Client.Tokens;

/// <summary>
/// The service default <see cref="ICalloutTokenProvider"/> (ADR 0002: a service connects as
/// just another OIDC principal, via the client-credentials grant). Promoted from the load
/// sample's <c>RefreshingCalloutConnection</c>/<c>DevOidcTokenFetcher</c> and generalized:
/// client credentials instead of ROPC, authority/OIDC-discovery support, an injectable
/// <see cref="HttpClient"/> (works with <c>IHttpClientFactory</c>), and <see cref="TimeProvider"/>
/// for deterministic testing.
/// <para>
/// Refresh semantics (ADR 0021 rule 5): re-mints <see cref="ClientCredentialsTokenProviderOptions.RefreshMargin"/>
/// before the cached token's <c>exp</c>; on a failed re-mint, keeps serving the stale token and
/// backs off <see cref="ClientCredentialsTokenProviderOptions.FailedRefreshBackoff"/> before
/// trying again, rather than hammering the IdP on every caller.
/// </para>
/// </summary>
public sealed class ClientCredentialsTokenProvider : ICalloutTokenProvider, IDisposable
{
    private readonly ClientCredentialsTokenProviderOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _mintLock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _cachedExpiry;
    private DateTimeOffset _nextRetryAfterFailure = DateTimeOffset.MinValue;
    private Uri? _resolvedTokenEndpoint;

    /// <summary>Initializes a new instance of the <see cref="ClientCredentialsTokenProvider"/> class.</summary>
    /// <param name="options">The provider's options. See <see cref="ClientCredentialsTokenProviderOptions"/> for the token-endpoint/authority mutual exclusivity rule.</param>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> to use. When null, a private instance is created and owned
    /// by this provider (disposed with it); pass one from <c>IHttpClientFactory</c> to share
    /// pooled connections instead.
    /// </param>
    /// <param name="timeProvider">The time provider; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="logger">The logger; defaults to <see cref="NullLogger"/>.</param>
    /// <exception cref="ArgumentException">Neither or both of <see cref="ClientCredentialsTokenProviderOptions.TokenEndpoint"/>/<see cref="ClientCredentialsTokenProviderOptions.Authority"/> are set, or <see cref="ClientCredentialsTokenProviderOptions.ClientId"/> is empty.</exception>
    public ClientCredentialsTokenProvider(
        ClientCredentialsTokenProviderOptions options,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null,
        ILogger<ClientCredentialsTokenProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TokenEndpoint is null == options.Authority is null)
        {
            throw new ArgumentException("Exactly one of TokenEndpoint or Authority must be set.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw new ArgumentException("ClientId must not be empty.", nameof(options));
        }

        _options = options;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ClientCredentialsTokenProvider>.Instance;
        _resolvedTokenEndpoint = options.TokenEndpoint;
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_cachedToken is not null && now < _cachedExpiry - _options.RefreshMargin)
        {
            return _cachedToken;
        }

        await _mintLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (_cachedToken is not null && now < _cachedExpiry - _options.RefreshMargin)
            {
                return _cachedToken;
            }

            if (_cachedToken is not null && now < _nextRetryAfterFailure)
            {
                // ADR 0021 rule 5: a failed re-mint keeps serving the stale token and backs
                // off rather than hammering the IdP on every caller.
                return _cachedToken;
            }

            try
            {
                var (token, expiry) = await MintAsync(cancellationToken).ConfigureAwait(false);
                _cachedToken = token;
                _cachedExpiry = expiry;
                return _cachedToken;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
            {
                _nextRetryAfterFailure = _timeProvider.GetUtcNow() + _options.FailedRefreshBackoff;
                _logger.LogWarning(ex, "Client-credentials token re-mint failed; retrying after {Backoff}, serving stale token meanwhile.", _options.FailedRefreshBackoff);
                return _cachedToken;
            }
        }
        finally
        {
            _mintLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask InvalidateAsync(CancellationToken cancellationToken)
    {
        await _mintLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cachedToken = null;
            _cachedExpiry = DateTimeOffset.MinValue;
            _nextRetryAfterFailure = DateTimeOffset.MinValue;
        }
        finally
        {
            _mintLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _mintLock.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<(string token, DateTimeOffset expiry)> MintAsync(CancellationToken cancellationToken)
    {
        var endpoint = await ResolveTokenEndpointAsync(cancellationToken).ConfigureAwait(false);
        var secret = await ResolveClientSecretAsync(cancellationToken).ConfigureAwait(false);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId,
        };
        if (secret is not null)
        {
            form["client_secret"] = secret;
        }

        if (_options.Scopes is { Count: > 0 })
        {
            form["scope"] = string.Join(' ', _options.Scopes);
        }

        if (!string.IsNullOrWhiteSpace(_options.Audience))
        {
            form["audience"] = _options.Audience;
        }

        using var body = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(endpoint, body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Token response from '{endpoint}' carried no body.");
        if (string.IsNullOrEmpty(payload.AccessToken))
        {
            throw new InvalidOperationException($"Token response from '{endpoint}' carried no access_token.");
        }

        var expiry = OidcTokenClaims.ReadExpiry(payload.AccessToken)
            ?? (payload.ExpiresInSeconds is { } seconds
                ? _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(seconds)
                : _timeProvider.GetUtcNow() + TimeSpan.FromMinutes(5));

        return (payload.AccessToken, expiry);
    }

    private async ValueTask<Uri> ResolveTokenEndpointAsync(CancellationToken cancellationToken)
    {
        if (_resolvedTokenEndpoint is not null)
        {
            return _resolvedTokenEndpoint;
        }

        var discoveryUrl = new Uri(_options.Authority!, ".well-known/openid-configuration");
        using var response = await _httpClient.GetAsync(discoveryUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<OidcDiscoveryDocument>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"OIDC discovery document at '{discoveryUrl}' was empty.");
        if (string.IsNullOrEmpty(document.TokenEndpoint))
        {
            throw new InvalidOperationException($"OIDC discovery document at '{discoveryUrl}' carried no token_endpoint.");
        }

        _resolvedTokenEndpoint = new Uri(document.TokenEndpoint);
        return _resolvedTokenEndpoint;
    }

    private async ValueTask<string?> ResolveClientSecretAsync(CancellationToken cancellationToken)
    {
        var secretRef = _options.ClientSecret;
        if (secretRef is null)
        {
            // Public client (no secret) is a valid client-credentials shape with some IdPs.
            return null;
        }

        // Only Inline/File are resolved directly here: this package does not depend on
        // AuthCallout.Secrets (doc 08 reference-isolation invariant), so K8sSecret/Vault
        // references are not resolvable in-process. A consumer needing those should resolve
        // the secret itself (e.g. via AuthCallout.Secrets.ISecretResolver) and plug the result
        // in through a DelegateTokenProvider instead.
        switch (secretRef.Kind)
        {
            case SecretRefKind.Inline:
                return secretRef.Value ?? throw new InvalidOperationException("Inline ClientSecret reference has no 'Value'.");
            case SecretRefKind.File:
            {
                var path = secretRef.Path ?? throw new InvalidOperationException("File ClientSecret reference has no 'Path'.");
                var raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                return raw.TrimEnd('\r', '\n');
            }

            default:
                throw new NotSupportedException(
                    $"ClientCredentialsTokenProvider resolves only Inline and File ClientSecret references in-process; '{secretRef.Kind}' requires a consumer-supplied ISecretResolver (see AuthCallout.Secrets) plugged in via DelegateTokenProvider.");
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresInSeconds);

    private sealed record OidcDiscoveryDocument([property: JsonPropertyName("token_endpoint")] string? TokenEndpoint);
}
