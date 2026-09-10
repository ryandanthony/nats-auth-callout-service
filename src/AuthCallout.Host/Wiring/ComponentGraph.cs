// -----------------------------------------------------------------------
// <copyright file="ComponentGraph.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;
using AuthCallout.Core.Pipeline;
using AuthCallout.Host.Lifecycle;
using AuthCallout.Host.Observability;
using AuthCallout.Identity.Jwks;
using AuthCallout.Identity.Validation;
using AuthCallout.Nats.Callout;
using AuthCallout.Nats.Events;
using NATS.Client.Core;

namespace AuthCallout.Host.Wiring;

/// <summary>
/// Builds every component collaborator downstream of the resolved bootstrap secrets and
/// active-config source (doc 08 §Process composition diagram: JWKS manager -&gt; token
/// validator -&gt; resolution pipeline -&gt; minter/crypto -&gt; event emitter -&gt; callout
/// responder -&gt; NATS connection manager), grouped into one factory so <c>Program.cs</c>'s
/// top-level statements stay within the repo's class-coupling analyzer limit. Every
/// collaborator here is built against its frozen Contracts interface, per the swarm ground
/// rules. The identity-store writer is selected by <see cref="IdentityStoreWriterFactory"/>
/// (real KV-backed P08 writer when an assertion keyset is configured, else the fail-closed
/// stand-in). Split across a few private helper methods (rather than one long method) purely
/// to keep each method's own class-coupling count under the repo's analyzer limit.
/// </summary>
public static class ComponentGraph
{
    /// <summary>Builds the whole component graph for one running instance.</summary>
    /// <param name="bootstrap">The validated bootstrap config.</param>
    /// <param name="secrets">The resolved bootstrap secrets.</param>
    /// <param name="activeConfigSource">The active-config source every config-reading component subscribes to.</param>
    /// <param name="stateMachine">The service state machine the connection manager reports into.</param>
    /// <param name="secretResolver">Resolves assertion-key public halves for the keys-bucket publish (P08 identity-store writer).</param>
    /// <returns>The fully-wired component graph.</returns>
    // CA2000 (dispose before scope loss) does not apply to any disposable constructed in this
    // method or its private helpers below (JwksCacheManager, UserJwtMinter, CalloutCrypto,
    // NatsConnection, NatsConnectionManager, HostMetrics): every one is handed to the caller
    // via the returned ComponentGraphResult, whose fields the DI container and
    // AuthCalloutHostedService own for the process lifetime and dispose on shutdown — the
    // same "hand off a live disposable" pattern AuthCallout.Secrets.SecretResolver documents
    // at its own CA2000 suppression.
#pragma warning disable CA2000
    public static ComponentGraphResult Build(
        BootstrapConfig bootstrap, ResolvedBootstrapSecrets secrets, ActiveConfigSource activeConfigSource, ServiceStateMachine stateMachine, ISecretResolver secretResolver)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(activeConfigSource);
        ArgumentNullException.ThrowIfNull(stateMachine);
        ArgumentNullException.ThrowIfNull(secretResolver);

        var pipelineParts = BuildPipeline(bootstrap, secrets, activeConfigSource);
        var natsParts = BuildNatsSide(bootstrap, secrets, activeConfigSource, stateMachine, pipelineParts, secretResolver);

        return new ComponentGraphResult(
            natsParts.IdentityStoreWriter, pipelineParts.JwksCacheManager, natsParts.Connection, natsParts.ConnectionManager, new HostMetrics(stateMachine));
    }

    private static PipelineParts BuildPipeline(BootstrapConfig bootstrap, ResolvedBootstrapSecrets secrets, ActiveConfigSource activeConfigSource)
    {
        var jwksCacheManager = new JwksCacheManager(activeConfigSource, bootstrap.Jwks);
        var tokenValidator = new TokenValidator(jwksCacheManager, jwksCacheManager);
        var resolutionPipeline = new ResolutionPipeline(tokenValidator);
        var minter = new UserJwtMinter(secrets.IssuerSeed);
        ICalloutCrypto calloutCrypto = secrets.XkeySeed is not null ? new CalloutCrypto(secrets.XkeySeed) : new NoOpCalloutCrypto();
        return new PipelineParts(jwksCacheManager, resolutionPipeline, minter, calloutCrypto);
    }

    private static NatsParts BuildNatsSide(
        BootstrapConfig bootstrap,
        ResolvedBootstrapSecrets secrets,
        ActiveConfigSource activeConfigSource,
        ServiceStateMachine stateMachine,
        PipelineParts pipelineParts,
        ISecretResolver secretResolver)
    {
        var natsConnection = new NatsConnection(BuildNatsOpts(bootstrap.NatsUrl, secrets.ServiceUserSeed));

        var identityStoreWriter = IdentityStoreWriterFactory.Create(natsConnection, secrets, secretResolver);

        var eventEmitter = new EventEmitter(
            natsConnection,
            activeConfigSource,
            onPublishFailure: static (ex, subject) => Console.Error.WriteLine($"WARN: event publish to '{subject}' failed: {ex.GetType().Name}"));

        var responderDependencies = new CalloutResponderDependencies(
            pipelineParts.ResolutionPipeline, activeConfigSource, pipelineParts.Minter, pipelineParts.CalloutCrypto, identityStoreWriter, eventEmitter);
        var responderOptions = new CalloutResponderOptions(bootstrap.Concurrency.MaxDegreeOfParallelism, bootstrap.Concurrency.PerRequestTimeout.Value);
        var responder = new CalloutResponder(responderDependencies, responderOptions, onDiagnostic: static msg => Console.Error.WriteLine($"DIAG: {msg}"));

        var connectionManager = new NatsConnectionManager(
            natsConnection, responder, stateMachine, onDiagnostic: static msg => Console.Error.WriteLine($"INFO: {msg}"));

        return new NatsParts(identityStoreWriter, natsConnection, connectionManager);
    }
#pragma warning restore CA2000

    private static NatsOpts BuildNatsOpts(string natsUrl, string? serviceUserSeed) => new()
    {
        Url = natsUrl,
        Name = "auth-callout",
        RetryOnInitialConnect = true, // ADR 0017 §1: always attempt recovery, including the first connect.
        AuthOpts = serviceUserSeed is not null ? new NatsAuthOpts { Seed = serviceUserSeed, NKey = NATS.NKeys.KeyPair.FromSeed(serviceUserSeed).GetPublicKey() } : NatsAuthOpts.Default,
    };

    private sealed record PipelineParts(JwksCacheManager JwksCacheManager, IResolutionPipeline ResolutionPipeline, IUserJwtMinter Minter, ICalloutCrypto CalloutCrypto);

    private sealed record NatsParts(IIdentityStoreWriter IdentityStoreWriter, NatsConnection Connection, NatsConnectionManager ConnectionManager);
}
