// -----------------------------------------------------------------------
// <copyright file="TestJwtIssuer.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace NatsAuthCallout.IntegrationTests;

/// <summary>
/// A minimal in-process OIDC-style issuer: signs RS256 JWTs and serves the
/// matching JWKS over HTTP so the callout's real <c>JwtTokenValidator</c> can
/// validate tokens without a full Keycloak container.
/// </summary>
public sealed class TestJwtIssuer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _securityKey;
    private readonly string _keyId;
    private readonly CancellationTokenSource _cts = new();
    private Task? _serveLoop;

    public TestJwtIssuer(string issuer, string audience)
    {
        Issuer = issuer;
        Audience = audience;

        _rsa = RSA.Create(2048);
        _keyId = Guid.NewGuid().ToString("N");
        _securityKey = new RsaSecurityKey(_rsa) { KeyId = _keyId };

        // Bind to a free loopback port.
        var port = GetFreePort();
        JwksUrl = $"http://127.0.0.1:{port}/certs";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    /// <summary>Gets the issuer (iss) value.</summary>
    public string Issuer { get; }

    /// <summary>Gets the audience the issuer stamps on tokens.</summary>
    public string Audience { get; }

    /// <summary>Gets the JWKS URL the validator should fetch keys from.</summary>
    public string JwksUrl { get; }

    public void Start()
    {
        _listener.Start();
        _serveLoop = Task.Run(ServeAsync);
    }

    /// <summary>
    /// Creates a signed token with the given claims (claim type to values).
    /// </summary>
    public string CreateToken(
        IReadOnlyDictionary<string, object> claims,
        TimeSpan? lifetime = null)
    {
        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(10)),
            SigningCredentials = new SigningCredentials(_securityKey, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>(claims, StringComparer.Ordinal),
        };
        return handler.CreateToken(descriptor);
    }

    private async Task ServeAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }

            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(_securityKey);
            jwk.Use = "sig";
            jwk.Alg = SecurityAlgorithms.RsaSha256;
            var json = $"{{\"keys\":[{System.Text.Json.JsonSerializer.Serialize(jwk)}]}}";
            var bytes = Encoding.UTF8.GetBytes(json);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 200;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private static int GetFreePort()
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
            // Already stopped.
        }

        if (_serveLoop is not null)
        {
            try
            {
                await _serveLoop;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _listener.Close();
        _rsa.Dispose();
        _cts.Dispose();
    }
}
