// -----------------------------------------------------------------------
// <copyright file="ApplicationSelector.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.IdentityModel.JsonWebTokens;
using NatsAuthCallout.Authorization;
using NatsAuthCallout.Configuration;

namespace NatsAuthCallout.Routing;

/// <summary>
/// Routes an inbound connection to the application whose permission model
/// should apply. Selection precedence:
/// <list type="number">
/// <item>service-account NKey allow-list match,</item>
/// <item>NATS connect username match,</item>
/// <item>bearer-token issuer (<c>iss</c>) match,</item>
/// <item>the configured default application.</item>
/// </list>
/// Returns <c>null</c> when nothing matches and no default is set.
/// </summary>
public sealed class ApplicationSelector
{
    private readonly IReadOnlyList<UserAuthorizer> _authorizers;
    private readonly UserAuthorizer? _default;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationSelector"/> class.
    /// </summary>
    /// <param name="authorizers">The per-application authorizers.</param>
    /// <param name="defaultApplication">The default application name, if any.</param>
    public ApplicationSelector(
        IReadOnlyList<UserAuthorizer> authorizers,
        string? defaultApplication)
    {
        _authorizers = authorizers ?? throw new ArgumentNullException(nameof(authorizers));
        _default = string.IsNullOrEmpty(defaultApplication)
            ? null
            : authorizers.FirstOrDefault(a =>
                string.Equals(a.Application.Name, defaultApplication, StringComparison.Ordinal))
              ?? throw new InvalidOperationException(
                  $"DefaultApplication '{defaultApplication}' does not match any configured application.");
    }

    /// <summary>
    /// Selects the authorizer for a connection.
    /// </summary>
    /// <param name="userNKey">The user NKey from the auth request.</param>
    /// <param name="connectUsername">The NATS connect username, if any.</param>
    /// <param name="bearerToken">The presented bearer token, if any.</param>
    /// <returns>The matching authorizer, or <c>null</c> when none applies.</returns>
    public UserAuthorizer? Select(string userNKey, string? connectUsername, string? bearerToken)
    {
        foreach (var authorizer in _authorizers)
        {
            if (authorizer.Application.Routing.ServiceAccountNKeys.Contains(userNKey, StringComparer.Ordinal))
            {
                return authorizer;
            }
        }

        if (!string.IsNullOrEmpty(connectUsername))
        {
            foreach (var authorizer in _authorizers)
            {
                if (authorizer.Application.Routing.ConnectUsernames.Contains(connectUsername, StringComparer.Ordinal))
                {
                    return authorizer;
                }
            }
        }

        var issuer = TryReadIssuer(bearerToken);
        if (!string.IsNullOrEmpty(issuer))
        {
            foreach (var authorizer in _authorizers)
            {
                if (authorizer.Application.Routing.TokenIssuers.Contains(issuer, StringComparer.Ordinal))
                {
                    return authorizer;
                }
            }
        }

        return _default;
    }

    /// <summary>
    /// Reads the <c>iss</c> claim from a JWT without validating it. Used only
    /// for routing; the selected application still fully validates the token.
    /// </summary>
    /// <param name="token">The bearer token, if any.</param>
    /// <returns>The issuer, or <c>null</c> when unavailable.</returns>
    internal static string? TryReadIssuer(string? token)
    {
        if (string.IsNullOrEmpty(token) || !token.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return new JsonWebToken(token).Issuer;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
