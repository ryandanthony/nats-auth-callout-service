// -----------------------------------------------------------------------
// <copyright file="ComponentGraphTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;
using AuthCallout.Host.Lifecycle;
using AuthCallout.Host.Wiring;
using NATS.Client.Core;
using NATS.NKeys;
using NSubstitute;

namespace AuthCallout.Host.Tests.Wiring;

/// <summary>
/// Proves <see cref="ComponentGraph.Build"/> opts into NATS.NET's implicit (TLS-first) TLS mode
/// whenever <see cref="BootstrapConfig.NatsUrl"/> uses the <c>tls://</c> scheme. Without this,
/// a server configured with <c>tls { handshake_first: true }</c> (required here so SNI-based
/// stream routing in front of the server works) sends its TLS handshake before the plaintext
/// INFO protocol message, and a NATS.NET client that has not set <see cref="NatsTlsOpts.Mode"/>
/// to <see cref="TlsMode.Implicit"/> never completes the handshake.
/// </summary>
public sealed class ComponentGraphTests
{
    [Fact]
    public void Build_WithTlsScheme_SetsImplicitTlsMode()
    {
        var bootstrap = HostTestBuilders.MinimalBootstrap() with { NatsUrl = "tls://nats-apps:4222" };

        var result = BuildGraph(bootstrap);

        Assert.Equal(TlsMode.Implicit, result.Connection.Opts.TlsOpts.Mode);
    }

    [Fact]
    public void Build_WithPlainScheme_LeavesDefaultTlsMode()
    {
        var bootstrap = HostTestBuilders.MinimalBootstrap() with { NatsUrl = "nats://nats-apps:4222" };

        var result = BuildGraph(bootstrap);

        Assert.Equal(NatsTlsOpts.Default.Mode, result.Connection.Opts.TlsOpts.Mode);
    }

    private static ComponentGraphResult BuildGraph(BootstrapConfig bootstrap)
    {
        using var issuer = KeyPair.CreatePair(PrefixByte.Account);
        var secrets = new ResolvedBootstrapSecrets(issuer.GetSeed(), ServiceUserSeed: null, XkeySeed: null, AssertionKeys: []);
        var activeConfigSource = new ActiveConfigSource();
        activeConfigSource.Apply(new ActiveConfig(bootstrap, HostTestBuilders.EmptyRouting(), 1));

        return ComponentGraph.Build(bootstrap, secrets, activeConfigSource, new ServiceStateMachine(prewarmRequired: false), Substitute.For<ISecretResolver>());
    }
}
