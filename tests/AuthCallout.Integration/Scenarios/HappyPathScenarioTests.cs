// -----------------------------------------------------------------------
// <copyright file="HappyPathScenarioTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using AuthCallout.Contracts.Seams;
using AuthCallout.Identity.Assertions;
using AuthCallout.Integration.Fixtures;
using AuthCallout.Integration.Support;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;
using NATS.NKeys;

namespace AuthCallout.Integration.Scenarios;

/// <summary>
/// Scenario 1 (P14 packet, the M2 milestone definition): a real Keycloak user token flows
/// through a real callout instance (<see cref="HostFixture"/>) fronting a real NATS server
/// with <c>auth_callout</c> configured — the client is granted exactly the profile's subjects
/// (positive and negative pub/sub probes), the KV token/assertion records are populated with
/// the right shape, an outcome event is observed, and the client fetches its own assertion and
/// publishes with the <c>Callout-Identity</c> header per doc 06's full identity sequence.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class HappyPathScenarioTests(IntegrationEnvironment environment) : IAsyncLifetime
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
    public async Task UserToken_GrantsExactlyProfileSubjects_PositiveAndNegativeProbes()
    {
        var token = await environment.Keycloak.GetPasswordGrantTokenAsync(
            IntegrationRealm.NotesClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);

        await using var client = await ConnectWithTokenAsync(token);

        // Positive: the editor profile's own grant (notes.dev.>).
        await AssertPublishAllowedAsync(client, "notes.dev.editor-check");

        // Negative: a subject the editor profile never grants.
        await AssertPublishDeniedAsync(client, "tasks.dev.forbidden");
    }

    [Fact]
    public async Task UserToken_PopulatesTokenAndAssertionKvRecords()
    {
        var token = await environment.Keycloak.GetPasswordGrantTokenAsync(
            IntegrationRealm.NotesClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);
        var jti = JwtInspector.ReadClaim(token, "jti");

        await using var client = await ConnectWithTokenAsync(token);
        await AssertPublishAllowedAsync(client, "notes.dev.kv-check"); // ensures the grant round-trip fully completed before we read KV.

        var kv = client.CreateKeyValueStoreContext();
        var assertionStore = await kv.GetStoreAsync("callout_apps_notes_assertions");

        // Token bucket is keyed by the callout-generated id, not jti - assert by scanning
        // for a value equal to the raw OIDC token rather than guessing the key (doc 07). The
        // publisher's own grant has no read access to that bucket (only autoAddSessionAccess
        // profiles do); use the service-user connection instead, which the callout itself
        // authenticates as and which is exempt from ordinary NATS permission checks entirely.
        var storedToken = await FindTokenRecordAsync(token);
        Assert.NotNull(storedToken);

        var storedAssertion = await assertionStore.GetEntryAsync<string>(jti);
        Assert.NotNull(storedAssertion.Value);
        Assert.Equal(3, storedAssertion.Value!.Split('.').Length); // header.payload.signature.
    }

    [Fact]
    public async Task UserToken_EmitsAuthOutcomeSuccessEvent()
    {
        var nats = environment.Nats;
        await using var subscriberConn = new NatsConnection(new NatsOpts
        {
            Url = nats.ConnectionUrl,
            AuthOpts = new NatsAuthOpts { Seed = nats.ServiceUserKey.Seed, NKey = nats.ServiceUserKey.PublicKey },
        });
        await subscriberConn.ConnectAsync();

        var eventTask = ReadOneEventAsync(subscriberConn, "notes.events.auth.success");
        await Task.Delay(200); // let the subscription actually establish before the auth triggers the publish.

        var token = await environment.Keycloak.GetPasswordGrantTokenAsync(
            IntegrationRealm.NotesClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);
        await using var client = await ConnectWithTokenAsync(token);

        var eventJson = await eventTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("\"outcome\":\"success\"", eventJson, StringComparison.Ordinal);
        Assert.Contains("\"application\":\"notes\"", eventJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_FetchesOwnAssertion_PublishesWithCalloutIdentityHeader_SubscriberVerifiesAndReadsToken()
    {
        var token = await environment.Keycloak.GetPasswordGrantTokenAsync(
            IntegrationRealm.NotesClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);
        var jti = JwtInspector.ReadClaim(token, "jti");

        await using var publisherClient = await ConnectWithTokenAsync(token);

        // Client fetches its own assertion via the granted self-lookup read (doc 06).
        var assertionsKv = publisherClient.CreateKeyValueStoreContext();
        var assertionsStore = await assertionsKv.GetStoreAsync("callout_apps_notes_assertions");
        var assertionEntry = await assertionsStore.GetEntryAsync<string>(jti);
        var assertion = assertionEntry.Value!;

        // Publisher publishes with the Callout-Identity header.
        var headers = new NatsHeaders { ["Callout-Identity"] = assertion };
        await publisherClient.PublishAsync("notes.dev.identity-relay", Encoding.UTF8.GetBytes("hello"), headers: headers);

        // A second notes-app connection (the same editor profile carries autoAddSessionAccess
        // for the notes token bucket, doc 07) subscribes, verifies the assertion via the
        // unconditional keys-bucket read every authenticated grant carries, and reads the
        // OIDC token by the assertion's id.
        var subscriberToken = await environment.Keycloak.GetPasswordGrantTokenAsync(
            IntegrationRealm.NotesClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);
        await using var subscriberClient = await ConnectWithTokenAsync(subscriberToken);

        var sub = subscriberClient.SubscribeAsync<byte[]>("notes.dev.identity-relay");
        var enumerator = sub.GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        var received = enumerator.Current;

        var receivedAssertion = received.Headers?["Callout-Identity"].ToString();
        Assert.False(string.IsNullOrEmpty(receivedAssertion));

        var keysStore = await subscriberClient.CreateKeyValueStoreContext().GetStoreAsync("callout_assertion_keys");
        var currentKid = (await keysStore.GetEntryAsync<string>("current")).Value!;
        var publicKey = (await keysStore.GetEntryAsync<string>(currentKid)).Value!;

        var verified = VerifyAssertion(receivedAssertion!, publicKey);
        Assert.NotNull(verified);
        Assert.Equal("notes", verified!.Application);

        var tokensStore = await subscriberClient.CreateKeyValueStoreContext().GetStoreAsync("callout_apps_notes_oidc_tokens");
        var oidcToken = await tokensStore.GetEntryAsync<string>(verified.Id);
        Assert.Equal(token, oidcToken.Value);
    }

    /// <summary>
    /// Verifies a signed assertion the same way a real subscriber would (doc 06 §Subscriber
    /// resolves caller identity): <see cref="AssertionSigner.Verify"/> is a pure function of
    /// the presented compact JWT and the verification public key (it never touches its own
    /// constructed keyset for verification, only for signing), so a throwaway instance
    /// constructed with an unrelated dummy signing key is exactly the production verification
    /// path — not a re-implementation of it.
    /// </summary>
    private static AssertionClaims? VerifyAssertion(string compactJwt, string publicKey)
    {
        var dummyKey = GeneratedNKeyPair.Create(PrefixByte.Account);
        using var verifier = new AssertionSigner([new ResolvedAssertionKey("dummy", dummyKey.Seed, Active: true)]);
        return verifier.Verify(compactJwt, publicKey);
    }

    private async Task<NatsConnection> ConnectWithTokenAsync(string token)
    {
        var conn = new NatsConnection(new NatsOpts
        {
            Url = environment.Nats.ConnectionUrl,
            AuthOpts = new NatsAuthOpts { Token = token },
        });
        await conn.ConnectAsync();
        return conn;
    }

    private async Task<string?> FindTokenRecordAsync(string expectedToken)
    {
        var nats = environment.Nats;
        await using var serviceConn = new NatsConnection(new NatsOpts
        {
            Url = nats.ConnectionUrl,
            AuthOpts = new NatsAuthOpts { Seed = nats.ServiceUserKey.Seed, NKey = nats.ServiceUserKey.PublicKey },
        });
        await serviceConn.ConnectAsync();
        var store = await serviceConn.CreateKeyValueStoreContext().GetStoreAsync("callout_apps_notes_oidc_tokens");
        return await FindEntryValueAsync(store, expectedToken);
    }

    private static async Task AssertPublishAllowedAsync(NatsConnection client, string subject)
    {
        var violation = await ProbePublishAsync(client, subject).ConfigureAwait(false);
        Assert.False(violation, $"Expected publish to '{subject}' to be allowed, but the server reported a permissions/authorization violation.");
    }

    private static async Task AssertPublishDeniedAsync(NatsConnection client, string subject)
    {
        var violation = await ProbePublishAsync(client, subject).ConfigureAwait(false);
        Assert.True(violation, $"Expected publish to '{subject}' to be denied, but no permissions/authorization violation was reported.");
    }

    /// <summary>Publishes a probe message and reports whether the server raised a permissions/authorization violation for it.</summary>
    private static async Task<bool> ProbePublishAsync(NatsConnection client, string subject)
    {
        var violation = false;
        ValueTask OnError(object? sender, NatsServerErrorEventArgs args)
        {
            if (args.Kind is NatsServerErrorKind.PermissionsViolation or NatsServerErrorKind.AuthorizationViolation)
            {
                violation = true;
            }

            return ValueTask.CompletedTask;
        }

        client.ServerError += OnError;
        try
        {
            await client.PublishAsync(subject, Encoding.UTF8.GetBytes("probe")).ConfigureAwait(false);
            await client.PingAsync().ConfigureAwait(false); // round-trip so an async permission-violation frame has arrived before we check.
        }
        finally
        {
            client.ServerError -= OnError;
        }

        return violation;
    }

    private static async Task<string?> FindEntryValueAsync(INatsKVStore store, string expectedValue)
    {
        await foreach (var key in store.GetKeysAsync())
        {
            var entry = await store.GetEntryAsync<string>(key);
            if (string.Equals(entry.Value, expectedValue, StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        return null;
    }

    private static async Task<string> ReadOneEventAsync(NatsConnection connection, string subject)
    {
        var enumerator = connection.SubscribeAsync<byte[]>(subject).GetAsyncEnumerator();
        if (!await enumerator.MoveNextAsync())
        {
            throw new InvalidOperationException("Subscription ended without receiving a message.");
        }

        return Encoding.UTF8.GetString(enumerator.Current.Data ?? []);
    }
}
