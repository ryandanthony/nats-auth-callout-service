// -----------------------------------------------------------------------
// <copyright file="UserAuthorizer.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NatsAuthCallout.Configuration;
using NatsAuthCallout.Jwt;

namespace NatsAuthCallout.Authorization;

/// <summary>
/// Turns an inbound connection (user NKey + optional bearer token) into an
/// <see cref="AuthDecision"/> for a single application, using that
/// application's data-driven permission model. Contains no transport code, so
/// it is fully unit-testable.
/// </summary>
public sealed class UserAuthorizer
{
    private const string ScopePlaceholder = "{scope}";

    private readonly ApplicationOptions _application;
    private readonly ITokenValidator _tokenValidator;
    private readonly TimeProvider _timeProvider;
    private readonly HashSet<string> _serviceAccounts;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserAuthorizer"/> class.
    /// </summary>
    /// <param name="application">The application configuration.</param>
    /// <param name="tokenValidator">The bearer-token validator.</param>
    /// <param name="timeProvider">The time provider.</param>
    public UserAuthorizer(
        ApplicationOptions application,
        ITokenValidator tokenValidator,
        TimeProvider timeProvider)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _tokenValidator = tokenValidator ?? throw new ArgumentNullException(nameof(tokenValidator));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _serviceAccounts = new HashSet<string>(
            application.Routing.ServiceAccountNKeys, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the application this authorizer serves.
    /// </summary>
    public ApplicationOptions Application => _application;

    /// <summary>
    /// Computes the authorization decision for a connection.
    /// </summary>
    /// <param name="userNKey">The user NKey from the auth request.</param>
    /// <param name="bearerToken">The presented bearer token, if any.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The decision to encode into a NATS user JWT.</returns>
    public async Task<AuthDecision> AuthorizeAsync(
        string userNKey,
        string? bearerToken,
        CancellationToken cancellationToken = default)
    {
        // 1. Trusted service account — full pub/sub, bypasses token validation.
        if (_serviceAccounts.Contains(userNKey))
        {
            return new AuthDecision(
                PrincipalKind.ServiceAccount,
                "service-account",
                [">"],
                [">"],
                ExpiresAt: null);
        }

        // 2. No token (or clearly not a JWT) — anonymous grant.
        if (string.IsNullOrEmpty(bearerToken) || !LooksLikeJwt(bearerToken))
        {
            return BuildAnonymous();
        }

        // 3. Validate the token. On any failure, fall back to anonymous.
        var outcome = await _tokenValidator.ValidateAsync(bearerToken, cancellationToken);
        if (!outcome.IsValid)
        {
            return BuildAnonymous();
        }

        var auth = _application.Permissions.Authenticated;
        var scopeValues = GetClaimValues(outcome.Claims, auth.ScopeClaim);

        if (auth.RequireScope && scopeValues.Count == 0)
        {
            return BuildAnonymous();
        }

        return BuildAuthenticated(outcome, scopeValues);
    }

    private static bool LooksLikeJwt(string token) =>
        token.Contains('.', StringComparison.Ordinal);

    private static IReadOnlyList<string> GetClaimValues(
        IReadOnlyDictionary<string, IReadOnlyList<string>> claims,
        string claimType)
    {
        if (string.IsNullOrEmpty(claimType))
        {
            return [];
        }

        return claims.TryGetValue(claimType, out var values) ? values : [];
    }

    private AuthDecision BuildAnonymous()
    {
        var grant = _application.Permissions.Anonymous;
        DateTimeOffset? expiry = grant.ExpirySeconds > 0
            ? _timeProvider.GetUtcNow().AddSeconds(grant.ExpirySeconds)
            : null;

        return new AuthDecision(
            PrincipalKind.Anonymous,
            "anonymous",
            [.. grant.Publish],
            [.. grant.Subscribe],
            expiry);
    }

    private AuthDecision BuildAuthenticated(
        TokenValidationOutcome outcome,
        IReadOnlyList<string> scopeValues)
    {
        var auth = _application.Permissions.Authenticated;

        var publish = new List<string>(auth.Publish);
        var subscribe = new List<string>(auth.Subscribe);

        foreach (var scope in scopeValues)
        {
            foreach (var pattern in auth.PublishPerScope)
            {
                publish.Add(Expand(pattern, scope));
            }

            foreach (var pattern in auth.SubscribePerScope)
            {
                subscribe.Add(Expand(pattern, scope));
            }
        }

        var name = GetClaimValues(outcome.Claims, auth.NameClaim).FirstOrDefault() ?? "authenticated";

        // Prefer the token's own expiry; fall back to configured lifetime.
        var expiry = outcome.ExpiresAt
            ?? _timeProvider.GetUtcNow().AddSeconds(auth.FallbackExpirySeconds);

        return new AuthDecision(
            PrincipalKind.Authenticated,
            name,
            publish,
            subscribe,
            expiry);
    }

    private static string Expand(string pattern, string scope) =>
        pattern.Replace(ScopePlaceholder, scope, StringComparison.Ordinal);
}
