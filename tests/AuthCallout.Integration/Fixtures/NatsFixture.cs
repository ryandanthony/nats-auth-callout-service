// -----------------------------------------------------------------------
// <copyright file="NatsFixture.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Integration.Support;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NATS.NKeys;

namespace AuthCallout.Integration.Fixtures;

/// <summary>
/// A real <c>nats:2.11</c> container (JetStream enabled) with <c>auth_callout</c> configured
/// exactly as the callout Host expects (doc 05/08, <see cref="NatsServerConfigBuilder"/>).
/// There is no dedicated Testcontainers.Nats module support for a custom config file/auth
/// setup, so this uses the generic <see cref="ContainerBuilder"/> directly (the same base type
/// <c>Testcontainers.Nats</c>'s own <c>NatsBuilder</c> is built on) with a resource-mapped
/// config file, mirroring the CLI example at
/// <see href="https://natsbyexample.com/examples/auth/callout/cli/"/>. One instance per test
/// collection (xUnit collection fixture): the container is expensive to start, and every
/// scenario in the collection shares it, using distinct NATS accounts/subjects/apps to avoid
/// cross-scenario interference.
/// </summary>
public sealed class NatsFixture : IAsyncLifetime
{
    private const int ClientPort = 4222;
    private const int MonitorPort = 8222;

    private IContainer? _container;

    /// <summary>Gets the callout's own account signing key (the <c>auth_callout.issuer</c> account NKey pair).</summary>
    /// <value>The issuer account key pair.</value>
    public GeneratedNKeyPair IssuerKey { get; } = GeneratedNKeyPair.Create(PrefixByte.Account);

    /// <summary>Gets the callout's own service-user NKey pair (the sole <c>auth_users</c> exemption).</summary>
    /// <value>The service-user key pair.</value>
    public GeneratedNKeyPair ServiceUserKey { get; } = GeneratedNKeyPair.Create(PrefixByte.User);

    /// <summary>
    /// Gets the xkey curve key pair used when this fixture opts into ADR 0018 encryption
    /// (set via <see cref="NatsFixture(bool)"/>). Null (the default) means the server was
    /// configured with no <c>auth_callout.xkey</c> — the common case for every scenario but
    /// the dedicated xkey suite.
    /// </summary>
    /// <value>The xkey curve key pair, or null.</value>
    public GeneratedNKeyPair? XkeyKey { get; }

    /// <summary>Initializes a new instance of the <see cref="NatsFixture"/> class.</summary>
    public NatsFixture()
        : this(withXkey: false)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="NatsFixture"/> class.</summary>
    /// <param name="withXkey">When true, generates an xkey curve pair and configures <c>auth_callout.xkey</c> with its public half (ADR 0018).</param>
    public NatsFixture(bool withXkey)
    {
        XkeyKey = withXkey ? GeneratedNKeyPair.Create(PrefixByte.Curve) : null;
    }

    /// <summary>Gets the mapped host port for the client connection.</summary>
    /// <value>The mapped client port.</value>
    public int MappedClientPort => _container?.GetMappedPublicPort(ClientPort) ?? throw NotStarted();

    /// <summary>Gets the <c>nats://</c> connection URL for the callout's own connection (or any test client).</summary>
    /// <value>The connection URL.</value>
    public string ConnectionUrl => $"nats://127.0.0.1:{MappedClientPort}";

    /// <summary>Gets the monitoring (HTTP) base URL, for <c>/healthz</c>/<c>/varz</c> diagnostics.</summary>
    /// <value>The monitoring base URL.</value>
    public string MonitoringUrl => $"http://127.0.0.1:{_container?.GetMappedPublicPort(MonitorPort) ?? throw NotStarted()}";

    /// <summary>Gets the underlying container, for scenario 12's stop/start lifecycle test.</summary>
    /// <value>The underlying container.</value>
    public IContainer Container => _container ?? throw NotStarted();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var configText = NatsServerConfigBuilder.Build(
            IssuerKey.PublicKey,
            ServiceUserKey.PublicKey,
            jetStreamStoreDirectory: "/tmp/nats-data",
            xkeyPublicKey: XkeyKey?.PublicKey);

        _container = new ContainerBuilder("nats:2.11")
            .WithPortBinding(ClientPort, assignRandomHostPort: true)
            .WithPortBinding(MonitorPort, assignRandomHostPort: true)
            .WithResourceMapping(System.Text.Encoding.UTF8.GetBytes(configText), "/etc/nats/server.conf")
            .WithCommand("-c", "/etc/nats/server.conf", "--http_port", MonitorPort.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPath("/healthz").ForPort(MonitorPort)))
            .Build();

        await _container.StartAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static InvalidOperationException NotStarted() =>
        new("NatsFixture has not been initialized yet.");
}
