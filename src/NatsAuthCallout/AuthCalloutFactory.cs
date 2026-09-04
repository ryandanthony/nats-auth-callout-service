// -----------------------------------------------------------------------
// <copyright file="AuthCalloutFactory.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NatsAuthCallout.Authorization;
using NatsAuthCallout.Configuration;
using NatsAuthCallout.Jwt;
using NatsAuthCallout.Routing;

namespace NatsAuthCallout;

/// <summary>
/// Builds the object graph (per-application authorizers, JWKS providers,
/// selector) from <see cref="AuthCalloutOptions"/>. Kept free of any DI
/// container so the CLI stays minimal and the graph is easy to unit test.
/// </summary>
public static class AuthCalloutFactory
{
    /// <summary>
    /// Creates the application selector for the supplied options.
    /// </summary>
    /// <param name="options">The validated service options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="tokenValidatorFactory">
    /// Optional override for constructing a token validator per application.
    /// Defaults to a JWKS-backed <see cref="JwtTokenValidator"/>. Tests use this
    /// to inject a fake validator.
    /// </param>
    /// <returns>The configured selector.</returns>
    public static ApplicationSelector CreateSelector(
        AuthCalloutOptions options,
        TimeProvider timeProvider,
        Func<ApplicationOptions, ITokenValidator>? tokenValidatorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        Validate(options);

        tokenValidatorFactory ??= app => CreateDefaultValidator(app, timeProvider);

        var authorizers = options.Applications
            .Select(app => new UserAuthorizer(app, tokenValidatorFactory(app), timeProvider))
            .ToList();

        return new ApplicationSelector(authorizers, options.DefaultApplication);
    }

    private static JwtTokenValidator CreateDefaultValidator(
        ApplicationOptions app,
        TimeProvider timeProvider)
    {
        var jwksUri = app.Jwt.ResolveJwksUri();
        var provider = new JwksProvider(
            jwksUri,
            TimeSpan.FromMinutes(app.Jwt.JwksCacheMinutes),
            timeProvider);

        return new JwtTokenValidator(app.Jwt, provider);
    }

    /// <summary>
    /// Validates the options, throwing when the configuration is unusable.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    public static void Validate(AuthCalloutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.IssuerSeed))
        {
            throw new InvalidOperationException("IssuerSeed must be configured.");
        }

        if (options.Applications.Count == 0)
        {
            throw new InvalidOperationException("At least one application must be configured.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var app in options.Applications)
        {
            if (string.IsNullOrWhiteSpace(app.Name))
            {
                throw new InvalidOperationException("Every application must have a Name.");
            }

            if (!names.Add(app.Name))
            {
                throw new InvalidOperationException($"Duplicate application name '{app.Name}'.");
            }
        }
    }
}
