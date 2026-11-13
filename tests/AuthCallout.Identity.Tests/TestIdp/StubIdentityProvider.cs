// -----------------------------------------------------------------------
// <copyright file="StubIdentityProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace AuthCallout.Identity.Tests.TestIdp;

/// <summary>
/// An in-proc, hand-rolled <see cref="HttpListener"/>-based stub OIDC issuer serving discovery
/// (<c>/.well-known/openid-configuration</c>) and JWKS (<c>/jwks</c>) with rotatable RSA keys,
/// and minting signed test tokens. No real network access, no external dependency (P06 packet
/// task: "Test IdP"). Advertised to P14 (integration suite) as a shared fixture per the P06
/// packet note; currently colocated here pending a coordinator-approved shared-fixture move.
/// </summary>
public sealed class StubIdentityProvider : IDisposable
{
    private readonly HttpListener _listener;
    private readonly Task _serveLoop;
    private readonly Lock _keysLock = new();
    private readonly List<(string kid, RSA rsa)> _keys = [];
    private readonly JwtSecurityTokenHandler _tokenHandler = new() { MapInboundClaims = false };
    private int _discoveryFetchCount;
    private int _jwksFetchCount;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="StubIdentityProvider"/> class, starting the listener and generating an initial signing key.</summary>
    public StubIdentityProvider()
    {
        var port = GetFreeTcpPort();
        Authority = $"http://127.0.0.1:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add(Authority + "/");
        _listener.Start();
        _serveLoop = Task.Run(ServeLoopAsync);

        AddKey("kid-1");
    }

    /// <summary>Gets the issuer authority (also the base URL) of the stub IdP.</summary>
    /// <value>The issuer authority.</value>
    public string Authority { get; }

    /// <summary>Gets the discovery document URL (<c>{Authority}/.well-known/openid-configuration</c>).</summary>
    /// <value>The discovery document URL.</value>
    public string DiscoveryUrl => Authority + "/.well-known/openid-configuration";

    /// <summary>Gets the direct JWKS URL (<c>{Authority}/jwks</c>), for the <c>jwksUri</c> override path.</summary>
    /// <value>The direct JWKS URL.</value>
    public string JwksUrl => Authority + "/jwks";

    /// <summary>Gets how many times the discovery document has been fetched.</summary>
    /// <value>The discovery fetch count.</value>
    public int DiscoveryFetchCount => Volatile.Read(ref _discoveryFetchCount);

    /// <summary>Gets how many times the JWKS document has been fetched.</summary>
    /// <value>The JWKS fetch count.</value>
    public int JwksFetchCount => Volatile.Read(ref _jwksFetchCount);

    /// <summary>Rotates to a brand-new signing key with the given <c>kid</c>, keeping prior keys published (so already-issued tokens keep verifying, mirroring real-world overlap).</summary>
    /// <param name="kid">The new key's <c>kid</c>.</param>
    public void AddKey(string kid)
    {
        ArgumentException.ThrowIfNullOrEmpty(kid);
        lock (_keysLock)
        {
            _keys.Add((kid, RSA.Create(2048)));
        }
    }

    /// <summary>Removes a previously added key by <c>kid</c> (simulates full key retirement, ADR 0014), so tokens it signed become unverifiable.</summary>
    /// <param name="kid">The <c>kid</c> to remove.</param>
    public void RemoveKey(string kid)
    {
        lock (_keysLock)
        {
            _keys.RemoveAll(k => string.Equals(k.kid, kid, StringComparison.Ordinal));
        }
    }

    /// <summary>Mints a signed JWT for this issuer with the given claims and expiry, signed with an existing (by default the most recently added) key.</summary>
    /// <param name="claims">Additional claims to include (beyond <c>iss</c>/<c>exp</c>, which are always set).</param>
    /// <param name="expires">The token's expiry.</param>
    /// <param name="notBefore">The token's <c>nbf</c>; defaults to now.</param>
    /// <param name="audience">The token's <c>aud</c>, when set.</param>
    /// <param name="signingKid">Which key to sign with; defaults to the most recently added key.</param>
    /// <returns>The encoded, signed JWT.</returns>
    public string IssueToken(
        IReadOnlyDictionary<string, object>? claims = null,
        DateTimeOffset? expires = null,
        DateTimeOffset? notBefore = null,
        string? audience = null,
        string? signingKid = null)
    {
        var (kid, rsa) = SelectKey(signingKid);
        var rsaKey = new RsaSecurityKey(rsa) { KeyId = kid };
        var credentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256);

        var payload = new JwtPayload(
            issuer: Authority,
            audience: audience,
            claims: [],
            notBefore: (notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-1)).UtcDateTime,
            expires: (expires ?? DateTimeOffset.UtcNow.AddMinutes(5)).UtcDateTime);

        foreach (var (key, value) in claims ?? new Dictionary<string, object>(StringComparer.Ordinal))
        {
            payload[key] = value;
        }

        var header = new JwtHeader(credentials);
        var jwt = new JwtSecurityToken(header, payload);
        return _tokenHandler.WriteToken(jwt);
    }

    /// <summary>Mints a JWT signed with a symmetric (HMAC) key derived from a real key's public bytes, simulating the classic algorithm-confusion attack (ADR 0016).</summary>
    /// <param name="claims">Additional claims to include.</param>
    /// <param name="expires">The token's expiry.</param>
    /// <param name="attackedKid">Which published key's public bytes to use as the forged HMAC secret; defaults to the most recent key.</param>
    /// <returns>The encoded, HS256-signed JWT, with a <c>kid</c> matching a real, published RSA key.</returns>
    public string IssueAlgorithmConfusionToken(
        IReadOnlyDictionary<string, object>? claims = null, DateTimeOffset? expires = null, string? attackedKid = null)
    {
        var (kid, rsa) = SelectKey(attackedKid);
        var publicKeyBytes = rsa.ExportSubjectPublicKeyInfo();
        var hmacKey = new SymmetricSecurityKey(publicKeyBytes) { KeyId = kid };
        var credentials = new SigningCredentials(hmacKey, SecurityAlgorithms.HmacSha256);

        var payload = new JwtPayload(
            issuer: Authority,
            audience: null,
            claims: [],
            notBefore: DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime,
            expires: (expires ?? DateTimeOffset.UtcNow.AddMinutes(5)).UtcDateTime);
        foreach (var (key, value) in claims ?? new Dictionary<string, object>(StringComparer.Ordinal))
        {
            payload[key] = value;
        }

        var header = new JwtHeader(credentials);
        var jwt = new JwtSecurityToken(header, payload);
        return _tokenHandler.WriteToken(jwt);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Stop + Close makes any in-flight GetContextAsync fault immediately, so the serve
        // loop's own catch block observes it and returns; observe the task here (fire and
        // forget, not awaited) purely so a faulted loop never becomes an unobserved task
        // exception, without Dispose itself blocking synchronously on async work.
        _listener.Stop();
        _listener.Close();
        _ = _serveLoop.ContinueWith(static _ => { }, TaskScheduler.Default);

        lock (_keysLock)
        {
            foreach (var (_, rsa) in _keys)
            {
                rsa.Dispose();
            }
        }
    }

    private (string kid, RSA rsa) SelectKey(string? kid)
    {
        lock (_keysLock)
        {
            if (kid is null)
            {
                return _keys[^1];
            }

            var match = _keys.Find(k => string.Equals(k.kid, kid, StringComparison.Ordinal));
            if (match.rsa is null)
            {
                throw new InvalidOperationException($"No such key '{kid}'.");
            }

            return match;
        }
    }

    private async Task ServeLoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            HandleRequest(context);
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? string.Empty;
        if (string.Equals(path, "/.well-known/openid-configuration", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _discoveryFetchCount);
            WriteJson(context, JsonSerializer.Serialize(new { issuer = Authority, jwks_uri = JwksUrl }));
        }
        else if (string.Equals(path, "/jwks", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _jwksFetchCount);
            WriteJson(context, BuildJwksJson());
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.Close();
        }
    }

    private string BuildJwksJson()
    {
        lock (_keysLock)
        {
            var jwks = _keys.Select(k =>
            {
                var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(k.rsa) { KeyId = k.kid });
                jwk.Alg = SecurityAlgorithms.RsaSha256;
                jwk.Use = "sig";
                return jwk;
            }).ToArray();
            return JsonSerializer.Serialize(new { keys = jwks });
        }
    }

    private static void WriteJson(HttpListenerContext context, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }

    private static int GetFreeTcpPort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }
}
