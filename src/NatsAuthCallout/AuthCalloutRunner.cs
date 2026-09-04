// -----------------------------------------------------------------------
// <copyright file="AuthCalloutRunner.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using NatsAuthCallout.Authorization;
using NatsAuthCallout.Configuration;
using NatsAuthCallout.Routing;
using NATS.Client.Core;
using NATS.Jwt;
using NATS.Jwt.Models;
using NATS.NKeys;

namespace NatsAuthCallout;

/// <summary>
/// Connects to NATS, subscribes to <c>$SYS.REQ.USER.AUTH</c>, and answers each
/// authorization request with a signed user JWT built from an
/// <see cref="AuthDecision"/>. Routes each connection to the correct
/// application via an <see cref="ApplicationSelector"/>. Runs inline (no
/// background-worker host).
/// </summary>
public sealed class AuthCalloutRunner : IAsyncDisposable
{
    /// <summary>The name of the ActivitySource/Meter emitted by this service.</summary>
    public const string TelemetryName = "NatsAuthCallout";

    private const string AuthSubject = "$SYS.REQ.USER.AUTH";
    private const int NKeyLogPrefixLength = 8;

    private static readonly Meter Meter = new(TelemetryName);
    private static readonly Counter<long> AuthRequests =
        Meter.CreateCounter<long>("nats_auth_callout.requests", unit: "{request}");
    private static readonly Counter<long> AuthErrors =
        Meter.CreateCounter<long>("nats_auth_callout.errors", unit: "{error}");
    private static readonly Counter<long> AuthDenied =
        Meter.CreateCounter<long>("nats_auth_callout.denied", unit: "{request}");

    private readonly AuthCalloutOptions _options;
    private readonly ApplicationSelector _selector;
    private readonly ILogger<AuthCalloutRunner> _logger;

    private NatsConnection? _connection;
    private INatsSub<string>? _subscription;
    private KeyPair? _issuerKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthCalloutRunner"/> class.
    /// </summary>
    /// <param name="options">The service options.</param>
    /// <param name="selector">The application router.</param>
    /// <param name="logger">The logger.</param>
    public AuthCalloutRunner(
        AuthCalloutOptions options,
        ApplicationSelector selector,
        ILogger<AuthCalloutRunner> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Connects and subscribes. Call before <see cref="RunAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.IssuerSeed))
        {
            throw new InvalidOperationException("IssuerSeed must be configured.");
        }

        _issuerKey = KeyPair.FromSeed(_options.IssuerSeed);

        var natsOpts = NatsOpts.Default with { Url = _options.Url, Name = TelemetryName };
        if (!string.IsNullOrEmpty(_options.ServiceUserSeed))
        {
            var seed = _options.ServiceUserSeed;
            natsOpts = natsOpts with
            {
                // Routing through AuthCredCallback ensures the public key is sent in
                // CONNECT so the server matches it against auth_users (bypass callout).
                AuthOpts = new NatsAuthOpts
                {
                    AuthCredCallback = (_, _) => ValueTask.FromResult(NatsAuthCred.FromNkey(seed)),
                },
            };
        }
        else
        {
            _logger.LogWarning("No ServiceUserSeed configured; connecting to NATS anonymously.");
        }

        _connection = new NatsConnection(natsOpts);
        await _connection.ConnectAsync();

        // Subscription lifetime is managed explicitly (see DisposeAsync); do not
        // tie it to a host-cancelled startup token.
        _subscription = await _connection.SubscribeCoreAsync<string>(
            AuthSubject,
            cancellationToken: CancellationToken.None);

        _logger.LogInformation(
            "Connected to {Url} and subscribed to {Subject}", _options.Url, AuthSubject);
    }

    /// <summary>
    /// Processes authorization requests until the token is cancelled.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_subscription is null)
        {
            throw new InvalidOperationException("StartAsync must be called before RunAsync.");
        }

        await foreach (var msg in _subscription.Msgs.ReadAllAsync(cancellationToken))
        {
            await HandleAsync(msg, cancellationToken);
        }

        _logger.LogInformation("Auth callout runner shutting down.");
    }

    private async Task HandleAsync(NatsMsg<string> msg, CancellationToken cancellationToken)
    {
        AuthRequests.Add(1);
        try
        {
            var requestClaims = NatsJwt.DecodeClaims<NatsAuthorizationRequestClaims>(msg.Data!);
            var request = requestClaims.AuthorizationRequest;

            var bearerToken = request.NatsConnectOptions.Token;
            var connectUsername = request.NatsConnectOptions.Username;

            var authorizer = _selector.Select(request.UserNKey, connectUsername, bearerToken);

            AuthDecision decision;
            string account;
            if (authorizer is null)
            {
                // No application matched and no default configured — deny with an
                // empty grant so the connection authenticates but can do nothing.
                AuthDenied.Add(1);
                _logger.LogWarning(
                    "No application matched connection for NKey {NKey}; issuing empty grant",
                    Prefix(request.UserNKey));
                decision = new AuthDecision(PrincipalKind.Anonymous, "denied", [], [], null);
                account = _options.Applications.FirstOrDefault()?.TargetAccount ?? "APP";
            }
            else
            {
                decision = await authorizer.AuthorizeAsync(
                    request.UserNKey, bearerToken, cancellationToken);
                account = authorizer.Application.TargetAccount;
            }

            var userJwt = EncodeUserJwt(request.UserNKey, account, decision);

            // The response wrapper must carry Subject = user NKey from the request
            // and Audience = the server's own id, otherwise NATS rejects the response.
            var responseClaims = new NatsAuthorizationResponseClaims
            {
                Subject = request.UserNKey,
                Audience = request.NatsServer.Id,
            };
            responseClaims.AuthorizationResponse.Jwt = userJwt;

            var responseToken = NatsJwt.EncodeAuthorizationResponseClaims(responseClaims, _issuerKey!);
            await msg.ReplyAsync(responseToken, cancellationToken: cancellationToken);

            _logger.LogDebug(
                "Authorized {Kind} user {NKey} (app={App}, name={Name})",
                decision.Kind,
                Prefix(request.UserNKey),
                authorizer?.Application.Name ?? "(none)",
                decision.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AuthErrors.Add(1);
            _logger.LogError(ex, "Auth callout error");
        }
    }

    private string EncodeUserJwt(string userNKey, string account, AuthDecision decision)
    {
        var userClaims = NatsJwt.NewUserClaims(userNKey);
        userClaims.Audience = account;
        userClaims.Name = decision.Name;
        userClaims.User.Pub.Allow = [.. decision.PublishAllow];
        userClaims.User.Sub.Allow = [.. decision.SubscribeAllow];

        if (decision.ExpiresAt is { } expiry)
        {
            userClaims.Expires = expiry;
        }

        return NatsJwt.EncodeUserClaims(userClaims, _issuerKey!);
    }

    private static string Prefix(string nkey) =>
        nkey.Length <= NKeyLogPrefixLength ? nkey : nkey[..NKeyLogPrefixLength];

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _issuerKey?.Dispose();
    }
}
