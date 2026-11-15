// -----------------------------------------------------------------------
// <copyright file="ClientLibraryRetrySemanticsTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using AuthCallout.Client.Tokens;
using AuthCallout.Integration.Fixtures;
using AuthCallout.Integration.Support;

namespace AuthCallout.Integration.Scenarios;

/// <summary>
/// P19 task 6 (integration half), scenario 2: ADR 0021's retry/auth-violation semantics driven
/// against a real callout (<see cref="HostFixture"/>) and a real NATS server, proving the three
/// rules the unit suite (<c>tests/AuthCallout.Client.Tests</c>) can only assert against a fake
/// transport: rule 2 (one forced-refresh retry then terminal failure on a real
/// "Authorization Violation"), rule 3 (immediate <see cref="CalloutAuthenticationRequiredException"/>
/// on a null token, never falling through to anonymous), and the reconnect-recovers-with-a-fresh-token
/// path rules 2/4/5 together describe.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ClientLibraryRetrySemanticsTests(IntegrationEnvironment environment) : IAsyncLifetime
{
    private HostFixture? _host;

    public async Task InitializeAsync()
    {
        var nats = environment.Nats;
        var keycloak = environment.Keycloak;
        _host = await HostFixture.StartAsync(
            nats,
            keycloak.CertificatePem,
            [TestApplications.NotesApp(keycloak.IssuerAuthority), TestApplications.TasksApp(keycloak.IssuerAuthority)],
            anonymous: null,
            CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    [Fact]
    public async Task ForgedToken_FailsAfterExactlyOneForcedRefreshRetry_FastAndTerminal()
    {
        // Two distinct, always-invalid tokens: the second proves InvalidateAsync's forced
        // refresh actually asked the provider again (not just retried the same cached value).
        // Well-formed JWT shape (sub/azp present, so the library can derive an inbox prefix),
        // but a forged signature the server's real JWKS validation rejects.
        var tokenProvider = new RecordingTokenProvider(ForgeToken("attempt-1"), ForgeToken("attempt-2"));

        await using var connection = new CalloutNatsConnection(new CalloutClientOptions
        {
            Url = environment.Nats.ConnectionUrl,
            ApplicationName = TestApplications.NotesAppName,
            TokenProvider = tokenProvider,
            Name = "forged-token-test",
        });

        var states = new List<CalloutConnectionState>();
        connection.StateChanged += (_, args) => states.Add(args.State);

        var started = DateTimeOffset.UtcNow;
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => connection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(30)));
        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.Equal(CalloutConnectionState.Failed, connection.State);
        Assert.Equal(1, tokenProvider.InvalidateCount);
        Assert.Equal(2, tokenProvider.TokenFetchCount); // initial attempt + exactly one forced-refresh retry.
        Assert.Contains(CalloutConnectionState.Connecting, states);
        Assert.Contains(CalloutConnectionState.Retrying, states);
        Assert.Contains(CalloutConnectionState.Failed, states);
        Assert.NotNull(ex);

        // ADR 0021 consequence: "fail fast and loud... (~1s with one retry) instead of
        // spinning". Generous bound for CI load, but proves no unbounded backoff/timeout.
        Assert.True(elapsed < TimeSpan.FromSeconds(25), $"Expected a fast terminal failure, took {elapsed}.");
    }

    [Fact]
    public async Task NullToken_FailsImmediately_WithCalloutAuthenticationRequiredException()
    {
        var tokenProvider = new RecordingTokenProvider((string?)null);

        await using var connection = new CalloutNatsConnection(new CalloutClientOptions
        {
            Url = environment.Nats.ConnectionUrl,
            ApplicationName = TestApplications.NotesAppName,
            TokenProvider = tokenProvider,
            Name = "null-token-test",
        });

        var exception = await Assert.ThrowsAsync<CalloutAuthenticationRequiredException>(
            () => connection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.NotNull(exception);
        Assert.Equal(CalloutConnectionState.Failed, connection.State);
        Assert.Equal(0, tokenProvider.InvalidateCount); // never retried; rule 3 is an immediate terminal failure, not an auth-violation retry.
    }

    [Fact]
    public async Task ExpiredTokenThenFreshToken_RetriesOnce_AndReachesConnected()
    {
        var validToken = await environment.Keycloak.GetPasswordGrantTokenAsync(
            IntegrationRealm.NotesClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);

        // The expired-looking value is a well-formed JWT (sub present, so the library can
        // derive an inbox prefix) with a forged signature: the real callout denies it as an
        // Authorization Violation regardless of why it is invalid, which is exactly the
        // real-callout surface this scenario proves (the unit suite already covers the
        // token-provider refresh-math half with a fake transport).
        var tokenProvider = new RecordingTokenProvider(ForgeToken("expiring-attempt"), validToken);

        await using var connection = new CalloutNatsConnection(new CalloutClientOptions
        {
            Url = environment.Nats.ConnectionUrl,
            ApplicationName = TestApplications.NotesAppName,
            TokenProvider = tokenProvider,
            Name = "recover-after-invalidate-test",
        });

        var states = new List<CalloutConnectionState>();
        connection.StateChanged += (_, args) => states.Add(args.State);

        await connection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(CalloutConnectionState.Connected, connection.State);
        Assert.Equal(1, tokenProvider.InvalidateCount);
        Assert.Equal(2, tokenProvider.TokenFetchCount);
        Assert.Contains(CalloutConnectionState.Retrying, states);
        Assert.Contains(CalloutConnectionState.Connected, states);
        Assert.NotNull(connection.Connection);
    }

    /// <summary>
    /// Builds a well-formed but forged JWT: a real <c>sub</c>/<c>azp</c> claim so the library
    /// can derive the scoped inbox prefix before it ever reaches the server, but a signature
    /// no real issuer ever produced — the real callout denies this as an "Authorization
    /// Violation" via its JWKS validation path, exactly the deny surface ADR 0021 rule 2
    /// describes.
    /// </summary>
    /// <param name="distinguisher">A value folded into the <c>sub</c>/<c>jti</c> claims so two calls never collide.</param>
    private static string ForgeToken(string distinguisher)
    {
        var header = Base64UrlEncode("""{"alg":"RS256","typ":"JWT"}""");
        var payload = Base64UrlEncode(
            $$"""{"sub":"forged-{{distinguisher}}","azp":"{{IntegrationRealm.NotesClientId}}","jti":"forged-jti-{{distinguisher}}","exp":9999999999}""");
        var signature = Base64UrlEncode($"forged-signature-{distinguisher}");
        return $"{header}.{payload}.{signature}";
    }

    private static string Base64UrlEncode(string value) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>
    /// A spy <see cref="ICalloutTokenProvider"/> wrapping a fixed sequence of tokens: each
    /// <see cref="GetTokenAsync"/> call advances through <paramref name="tokens"/> (clamped to
    /// the last entry once exhausted), and both calls are counted so scenarios can assert the
    /// exact ADR 0021 retry shape (one invalidate, exactly one re-fetch).
    /// </summary>
    private sealed class RecordingTokenProvider : ICalloutTokenProvider
    {
        private readonly string?[] _tokens;
        private int _tokenIndex;

        public RecordingTokenProvider(params string?[] tokens)
        {
            _tokens = tokens.Length == 0 ? [null] : tokens;
        }

        public int TokenFetchCount { get; private set; }

        public int InvalidateCount { get; private set; }

        public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken)
        {
            TokenFetchCount++;
            var index = Math.Min(_tokenIndex, _tokens.Length - 1);
            return ValueTask.FromResult(_tokens[index]);
        }

        public ValueTask InvalidateAsync(CancellationToken cancellationToken)
        {
            InvalidateCount++;
            _tokenIndex = Math.Min(_tokenIndex + 1, _tokens.Length - 1);
            return ValueTask.CompletedTask;
        }
    }
}
