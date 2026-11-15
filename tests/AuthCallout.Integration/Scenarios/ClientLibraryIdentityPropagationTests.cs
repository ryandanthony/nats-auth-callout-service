// -----------------------------------------------------------------------
// <copyright file="ClientLibraryIdentityPropagationTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using AuthCallout.Client.Identity;
using AuthCallout.Client.Tokens;
using AuthCallout.Contracts.Config;
using AuthCallout.Integration.Fixtures;
using AuthCallout.Integration.Support;
using NATS.Client.Core;
using NATS.Net;

namespace AuthCallout.Integration.Scenarios;

/// <summary>
/// P19 task 6 (integration half), scenarios 4-6: identity propagation driven end-to-end through
/// the library only — client discovers + fetches its self-assertion + attaches the
/// <see cref="CalloutIdentityHeader"/>, and a subscriber verifies it via
/// <see cref="CalloutIdentityResolver"/>/<see cref="ICalloutIdentityVerifier"/> and resolves the
/// caller's OIDC token through <see cref="CalloutIdentityResolver.ResolveOidcTokenAsync"/>
/// (requires <c>autoAddSessionAccess</c>, already set on <see cref="TestApplications"/>'s
/// profiles). Also covers a forged/expired assertion (verifier returns null, never throws) and
/// an application with identity propagation disabled (discovery says <c>enabled: false</c>; the
/// header-attach helper no-ops).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ClientLibraryIdentityPropagationTests(IntegrationEnvironment environment) : IAsyncLifetime
{
    private HostFixture? _host;

    public async Task InitializeAsync()
    {
        var nats = environment.Nats;
        var keycloak = environment.Keycloak;
        _host = await HostFixture.StartAsync(
            nats,
            keycloak.CertificatePem,
            [
                TestApplications.NotesApp(keycloak.IssuerAuthority),
                TestApplications.TasksApp(keycloak.IssuerAuthority),
                PropagationDisabledApp(keycloak.IssuerAuthority),
            ],
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
    public async Task ClientAttachesHeaderViaLibrary_SubscriberVerifiesAndResolvesOidcToken()
    {
        var publisherToken = await environment.Keycloak.GetPasswordGrantTokenAsync(
            IntegrationRealm.NotesClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);

        await using var publisherConnection = new CalloutNatsConnection(new CalloutClientOptions
        {
            Url = environment.Nats.ConnectionUrl,
            ApplicationName = TestApplications.NotesAppName,
            TokenProvider = new StaticTokenProvider(publisherToken),
            Name = "identity-propagation-publisher",
        });
        await publisherConnection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var publisherClient = publisherConnection.Connection!;
        var publisherKv = publisherClient.CreateKeyValueStoreContext();

        var descriptor = await CalloutDiscovery.FetchAsync(publisherKv, TestApplications.NotesAppName);
        Assert.True(descriptor.IdentityPropagation.Enabled);

        using var identityHeader = new CalloutIdentityHeader(publisherKv, descriptor, publisherToken);

        // Second notes-app connection subscribes before the publish (NATS core pub/sub has no
        // replay), mirroring HappyPathScenarioTests' own identity round-trip test.
        var subscriberToken = await environment.Keycloak.GetPasswordGrantTokenAsync(
            IntegrationRealm.NotesClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);
        await using var subscriberConnection = new CalloutNatsConnection(new CalloutClientOptions
        {
            Url = environment.Nats.ConnectionUrl,
            ApplicationName = TestApplications.NotesAppName,
            TokenProvider = new StaticTokenProvider(subscriberToken),
            Name = "identity-propagation-subscriber",
        });
        await subscriberConnection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var subscriberClient = subscriberConnection.Connection!;

        var enumerator = subscriberClient.SubscribeAsync<byte[]>("notes.dev.client-identity-relay").GetAsyncEnumerator();
        var receiveTask = enumerator.MoveNextAsync().AsTask();
        await Task.Delay(200);

        var headers = await identityHeader.AttachAsync(headers: null);
        Assert.NotNull(headers);
        await publisherClient.PublishAsync("notes.dev.client-identity-relay", "hello"u8.ToArray(), headers: headers);

        Assert.True(await receiveTask.WaitAsync(TimeSpan.FromSeconds(5)));
        var receivedHeaderValue = enumerator.Current.Headers?[descriptor.IdentityPropagation.Header!].ToString();
        Assert.False(string.IsNullOrEmpty(receivedHeaderValue));

        var subscriberKv = subscriberClient.CreateKeyValueStoreContext();
        var subscriberDescriptor = await CalloutDiscovery.FetchAsync(subscriberKv, TestApplications.NotesAppName);
        using var resolver = new CalloutIdentityResolver(subscriberKv, subscriberDescriptor);
        var verified = await resolver.VerifyAsync(receivedHeaderValue!);

        Assert.NotNull(verified);
        Assert.Equal(TestApplications.NotesAppName, verified!.Application);

        var resolvedOidcToken = await resolver.ResolveOidcTokenAsync(verified.Id);
        Assert.Equal(publisherToken, resolvedOidcToken);
    }

    [Fact]
    public async Task VerifierRejectsForgedAssertion_WithoutThrowing()
    {
        var token = await environment.Keycloak.GetPasswordGrantTokenAsync(
            IntegrationRealm.NotesClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);

        await using var connection = new CalloutNatsConnection(new CalloutClientOptions
        {
            Url = environment.Nats.ConnectionUrl,
            ApplicationName = TestApplications.NotesAppName,
            TokenProvider = new StaticTokenProvider(token),
            Name = "forged-assertion-test",
        });
        await connection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var kv = connection.Connection!.CreateKeyValueStoreContext();
        var descriptor = await CalloutDiscovery.FetchAsync(kv, TestApplications.NotesAppName);
        using var resolver = new CalloutIdentityResolver(kv, descriptor);

        // Not a JWT at all (no kid in a header section).
        Assert.Null(await resolver.VerifyAsync("not-a-jwt"));

        // Well-formed JWT shape, but garbage signature/claims with an unknown kid.
        var forged = $"{Base64UrlEncode("""{"alg":"EdDSA","typ":"JWT","kid":"unknown-kid"}""")}." +
            $"{Base64UrlEncode("""{"id":"forged","sub":"attacker","app":"notes","iss":"forged-issuer","exp":9999999999}""")}." +
            $"{Base64UrlEncode("forged-signature-bytes")}";
        Assert.Null(await resolver.VerifyAsync(forged));
    }

    [Fact]
    public async Task PropagationDisabledApp_DiscoveryReportsDisabled_AndHeaderAttachNoOps()
    {
        var keycloak = environment.Keycloak;
        var token = await keycloak.GetClientCredentialsTokenAsync(IntegrationRealm.NoPropagationServiceClientId, IntegrationRealm.ServiceClientSecret);

        await using var connection = new CalloutNatsConnection(new CalloutClientOptions
        {
            Url = environment.Nats.ConnectionUrl,
            ApplicationName = PropagationDisabledAppName,
            TokenProvider = new StaticTokenProvider(token),
            Name = "propagation-disabled-test",
        });
        await connection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var kv = connection.Connection!.CreateKeyValueStoreContext();

        var descriptor = await CalloutDiscovery.FetchAsync(kv, PropagationDisabledAppName);
        Assert.False(descriptor.IdentityPropagation.Enabled);

        using var identityHeader = new CalloutIdentityHeader(kv, descriptor, token);
        var headers = await identityHeader.AttachAsync(headers: null);
        Assert.Null(headers);

        var existing = new NatsHeaders { ["X-Already-There"] = "yes" };
        var unchanged = await identityHeader.AttachAsync(existing);
        Assert.Same(existing, unchanged);
    }

    private const string PropagationDisabledAppName = "no-propagation";

    /// <summary>
    /// A third application with a distinct client id (<see cref="IntegrationRealm.NoPropagationServiceClientId"/>)
    /// so this app's grant is independent of <c>tasks</c>'s — with
    /// <c>IdentityPropagation.Enabled: false</c> (scenario 6). No profile here sets
    /// <c>AutoAddSessionAccess</c>: that is a config-load error when propagation is disabled
    /// (<see cref="IdentityPropagationConfig"/>'s own doc comment), so this app intentionally
    /// omits it.
    /// </summary>
    private static Application PropagationDisabledApp(string issuerAuthority) => new(
        Name: PropagationDisabledAppName,
        TargetAccount: NatsServerConfigBuilder.TasksAccountName,
        Issuer: TestApplications.Issuer(issuerAuthority),
        Match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["azp"] = [IntegrationRealm.NoPropagationServiceClientId],
        }),
        AnonymousProfile: null,
        Profiles:
        [
            new Profile(
                Name: "service",
                Match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["azp"] = [IntegrationRealm.NoPropagationServiceClientId],
                }),
                Grant: new Grant(Publish: ["tasks.svc.>"], Subscribe: ["tasks.svc.>"]),
                NameClaim: "azp",
                Variables: [],
                PublishTemplates: [],
                SubscribeTemplates: [],
                AutoAddSessionAccess: false),
        ],
        IdentityPropagation: new IdentityPropagationConfig(Enabled: false),
        Events: null);

    private static string Base64UrlEncode(string value) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
