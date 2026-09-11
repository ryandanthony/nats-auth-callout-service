// -----------------------------------------------------------------------
// <copyright file="HostFixture.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;
using AuthCallout.Contracts.Seams;
using AuthCallout.Core.Pipeline;
using AuthCallout.Identity.Assertions;
using AuthCallout.Identity.Jwks;
using AuthCallout.Identity.Validation;
using AuthCallout.Integration.Fixtures;
using AuthCallout.Nats.Callout;
using AuthCallout.Nats.Events;
using AuthCallout.Nats.Identity;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;
using NATS.NKeys;

namespace AuthCallout.Integration.Support;

/// <summary>
/// Runs the real callout component graph in-process against a <see cref="NatsFixture"/> and
/// <see cref="KeycloakFixture"/> — the same collaborators <c>AuthCallout.Host.Wiring.ComponentGraph</c>
/// wires in production (<c>JwksCacheManager</c> → <c>TokenValidator</c> →
/// <c>ResolutionPipeline</c> → <c>UserJwtMinter</c>/<c>CalloutCrypto</c> → identity-store
/// writer → <c>EventEmitter</c> → <c>CalloutResponder</c>), driven directly rather than via
/// the process-level <c>Program.cs</c>/generic-host bootstrap: this suite needs to inject one
/// test seam <c>ComponentGraph</c> does not expose — <c>JwksCacheManager</c>'s
/// <see cref="IssuerConfigurationManagerFactory(HttpClient)"/> constructor, pointed at an
/// <see cref="HttpClient"/> that trusts <see cref="KeycloakFixture"/>'s self-signed CA
/// (<see cref="TrustedHttpClientFactory"/>) — the production parameterless constructor always
/// requires a publicly-trusted HTTPS certificate chain, which a throwaway test fixture's
/// self-signed cert is not, by design (ADR 0016 §Fail closed posture: JWKS fetch never
/// relaxes transport trust). Every other collaborator here is the exact same production type
/// with the exact same wiring as <c>ComponentGraph.Build</c>, so this suite is a real proof of
/// the production request path, not a re-implementation of it.
/// </summary>
public sealed class HostFixture : IAsyncDisposable
{
    private readonly HttpClient _jwksHttpClient;
    private readonly JwksCacheManager _jwksCacheManager;
    private readonly UserJwtMinter _minter;
    private readonly ICalloutCrypto _crypto;
    private readonly AssertionSigner _assertionSigner;
    private readonly NatsConnection _connection;
    private readonly CalloutResponder _responder;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _runTask;

    private HostFixture(HostFixtureParts parts)
    {
        _jwksHttpClient = parts.JwksHttpClient;
        _jwksCacheManager = parts.JwksCacheManager;
        _minter = parts.Minter;
        _crypto = parts.Crypto;
        _assertionSigner = parts.AssertionSigner;
        _connection = parts.Connection;
        _responder = parts.Responder;
        IdentityStoreWriter = parts.IdentityStoreWriter;
        _runTask = Task.Run(() => _responder.RunAsync(_connection, _cts.Token));
    }

    /// <summary>Gets the identity-store writer, exposed for scenarios that assert KV contents directly.</summary>
    /// <value>The identity-store writer.</value>
    public KvIdentityStoreWriter IdentityStoreWriter { get; }

    /// <summary>
    /// Builds and starts a full callout instance: connects the service-user NATS connection,
    /// prewarms JWKS, provisions every identity-store KV bucket, publishes assertion keys and
    /// discovery, and starts the callout responder loop.
    /// </summary>
    /// <param name="nats">The NATS fixture to run against.</param>
    /// <param name="keycloakCertificatePem">The Keycloak fixture's self-signed CA, trusted for JWKS fetches (see the type doc comment).</param>
    /// <param name="applications">The routing aggregate's applications.</param>
    /// <param name="anonymous">The global anonymous-grant config. Null uses <see cref="NatsServerConfigBuilder.AnonAccountName"/> with a 5-minute expiry.</param>
    /// <param name="cancellationToken">Cancellation for startup.</param>
    /// <returns>The running fixture.</returns>
    // CA2000 (dispose before scope loss) does not apply to any disposable constructed in this
    // method or its private helpers below: every one (the JWKS HttpClient, JwksCacheManager,
    // UserJwtMinter, CalloutCrypto, AssertionSigner, NatsConnection) is handed to the returned
    // HostFixture, which owns and disposes them all in DisposeAsync - the same "hand off a
    // live disposable" pattern AuthCallout.Host.Wiring.ComponentGraph documents at its own
    // CA2000 suppression (this type is this suite's equivalent of that production factory).
    // CA1506 (class coupling): this method's whole job is wiring the same production
    // component graph AuthCallout.Host.Wiring.ComponentGraph.Build wires (JWKS -> validator
    // -> pipeline -> minter/crypto -> identity store -> responder), which legitimately
    // touches every collaborator type along that chain; ComponentGraph.Build itself is
    // exempt from this repo's coupling limit as its own production wiring entry point, and
    // this test-fixture equivalent inherits the same shape for the same reason.
#pragma warning disable CA2000, CA1506
    public static async Task<HostFixture> StartAsync(
        NatsFixture nats,
        string keycloakCertificatePem,
        IReadOnlyList<Application> applications,
        AnonymousGlobalConfig? anonymous,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nats);
        ArgumentException.ThrowIfNullOrWhiteSpace(keycloakCertificatePem);
        ArgumentNullException.ThrowIfNull(applications);

        var bootstrap = BuildBootstrap(anonymous);
        var activeConfigSource = new StaticActiveConfigSource(new ActiveConfig(bootstrap, new RoutingConfig(applications), Version: 1));

        var pipeline = await BuildPipelineAsync(nats, keycloakCertificatePem, bootstrap, activeConfigSource, cancellationToken).ConfigureAwait(false);
        var connection = await ConnectAsync(nats, cancellationToken).ConfigureAwait(false);
        var identity = await BuildIdentityStoreAsync(connection, activeConfigSource, cancellationToken).ConfigureAwait(false);

        var eventEmitter = new EventEmitter(connection, activeConfigSource);
        var responderDependencies = new CalloutResponderDependencies(
            pipeline.ResolutionPipeline, activeConfigSource, pipeline.Minter, pipeline.Crypto, identity.Writer, eventEmitter);
        var responderOptions = new CalloutResponderOptions(bootstrap.Concurrency.MaxDegreeOfParallelism, bootstrap.Concurrency.PerRequestTimeout.Value);
        var responder = new CalloutResponder(
            responderDependencies, responderOptions, onDiagnostic: static msg => Console.Error.WriteLine($"[HostFixture] {msg}"));

        return new HostFixture(new HostFixtureParts(
            pipeline.JwksHttpClient, pipeline.JwksCacheManager, pipeline.Minter, pipeline.Crypto, identity.Signer, connection, responder, identity.Writer));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
#pragma warning disable VSTHRD003 // _runTask was started by this same instance in its constructor; awaiting it here is the intended join point, mirroring NatsConnectionManager's own equivalent suppression.
        try
        {
            await _runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: the responder loop observes _cts.
        }
#pragma warning restore VSTHRD003

        _jwksCacheManager.Dispose();
        _jwksHttpClient.Dispose();
        _minter.Dispose();
        (_crypto as IDisposable)?.Dispose();
        _assertionSigner.Dispose();
        await _connection.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    private static async Task<PipelineParts> BuildPipelineAsync(
        NatsFixture nats, string keycloakCertificatePem, BootstrapConfig bootstrap, StaticActiveConfigSource activeConfigSource, CancellationToken cancellationToken)
    {
        var jwksHttpClient = TrustedHttpClientFactory.Create(keycloakCertificatePem);
        var fetcherFactory = new IssuerConfigurationManagerFactory(jwksHttpClient);
        var jwksCacheManager = new JwksCacheManager(activeConfigSource, bootstrap.Jwks, fetcherFactory);
        await jwksCacheManager.PrewarmAllAsync(cancellationToken).ConfigureAwait(false);

        var tokenValidator = new TokenValidator(jwksCacheManager, jwksCacheManager);
        var resolutionPipeline = new ResolutionPipeline(tokenValidator);
        var minter = new UserJwtMinter(nats.IssuerKey.Seed);
        ICalloutCrypto crypto = nats.XkeyKey is { } xkey ? new CalloutCrypto(xkey.Seed) : new NoOpCalloutCrypto();

        return new PipelineParts(jwksHttpClient, jwksCacheManager, resolutionPipeline, minter, crypto);
    }

    private static async Task<NatsConnection> ConnectAsync(NatsFixture nats, CancellationToken cancellationToken)
    {
        var connection = new NatsConnection(new NatsOpts
        {
            Url = nats.ConnectionUrl,
            Name = "auth-callout-integration",
            AuthOpts = new NatsAuthOpts { Seed = nats.ServiceUserKey.Seed, NKey = nats.ServiceUserKey.PublicKey },
        });
        await connection.ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task<IdentityParts> BuildIdentityStoreAsync(
        NatsConnection connection, StaticActiveConfigSource activeConfigSource, CancellationToken cancellationToken)
    {
        var assertionKey = GeneratedNKeyPair.Create(PrefixByte.Account);
        var assertionSigner = new AssertionSigner([new ResolvedAssertionKey("kid-1", assertionKey.Seed, Active: true)]);
        INatsKVContext kvContext = connection.CreateKeyValueStoreContext();
        var writer = new KvIdentityStoreWriter(kvContext, assertionSigner, new InlineSecretResolver(assertionKey.Seed));

        await writer.ProvisionBucketsAsync(activeConfigSource.Current!.Routing, cancellationToken).ConfigureAwait(false);
        await writer.PublishKeysAndDiscoveryAsync(activeConfigSource.Current, cancellationToken).ConfigureAwait(false);

        return new IdentityParts(assertionSigner, writer);
    }
#pragma warning restore CA2000, CA1506

    private static BootstrapConfig BuildBootstrap(AnonymousGlobalConfig? anonymous) => new(
        NatsUrl: "nats://unused-direct-connection-owns-transport",
        IssuerSeed: SecretRef.Inline("unused-direct-seed-owns-signing"),
        ServiceUserSeed: null,
        XkeySeed: null,
        AssertionKeys: null,
        Source: SourceConfig.Files(new FilesSourceSettings("/unused"), DurationString.Parse("60s"), DurationString.Parse("15m"), DurationString.Parse("5s")),
        Jwks: new JwksSettings(DurationString.Parse("5m"), DurationString.Parse("5s"), PrewarmRequired: true),
        Concurrency: new ConcurrencySettings(MaxDegreeOfParallelism: 16, PerRequestTimeout: DurationString.Parse("2s"), QueueCapacity: null),
        Anonymous: anonymous ?? new AnonymousGlobalConfig(NatsServerConfigBuilder.AnonAccountName, DurationString.Parse("5m"), Events: null));

    private sealed record HostFixtureParts(
        HttpClient JwksHttpClient,
        JwksCacheManager JwksCacheManager,
        UserJwtMinter Minter,
        ICalloutCrypto Crypto,
        AssertionSigner AssertionSigner,
        NatsConnection Connection,
        CalloutResponder Responder,
        KvIdentityStoreWriter IdentityStoreWriter);

    private sealed record PipelineParts(
        HttpClient JwksHttpClient, JwksCacheManager JwksCacheManager, IResolutionPipeline ResolutionPipeline, UserJwtMinter Minter, ICalloutCrypto Crypto);

    private sealed record IdentityParts(AssertionSigner Signer, KvIdentityStoreWriter Writer);

    /// <summary>A fixed, never-changing <see cref="IActiveConfigSource"/> — this suite drives one static config per <see cref="HostFixture"/> instance rather than exercising hot-reload (that is scenario 7's own concern, over the files provider directly).</summary>
    private sealed class StaticActiveConfigSource : IActiveConfigSource
    {
        public StaticActiveConfigSource(ActiveConfig config) => Current = config;

        public ActiveConfig? Current { get; }

#pragma warning disable CS0067 // Never raised: Current never changes for the lifetime of a StaticActiveConfigSource.
        public event EventHandler<ActiveConfigChangedEventArgs>? Changed;
#pragma warning restore CS0067
    }

    /// <summary>A minimal <see cref="ISecretResolver"/> resolving exactly the one inline assertion-key seed <see cref="KvIdentityStoreWriter"/> needs for the keys-bucket public-key publish.</summary>
    private sealed class InlineSecretResolver(string seed) : ISecretResolver
    {
        public Task<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken) =>
            Task.FromResult(new ResolvedSecret(seed));
    }
}
