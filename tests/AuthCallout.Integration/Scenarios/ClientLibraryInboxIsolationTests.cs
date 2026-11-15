// -----------------------------------------------------------------------
// <copyright file="ClientLibraryInboxIsolationTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using AuthCallout.Client.Tokens;
using AuthCallout.Integration.Fixtures;
using AuthCallout.Integration.Support;
using NATS.Client.Core;
using NATS.Net;

namespace AuthCallout.Integration.Scenarios;

/// <summary>
/// P19 task 6 (integration half), scenario 3: scoped inbox isolation driven through the library
/// (<see cref="CalloutNatsOpts.CreateAsync"/>'s derived <c>InboxPrefix</c>, ADR 0013 S2 fix) —
/// two authenticated connections for distinct principals can each use their own inbox (proven
/// via a real JetStream KV request/reply, which rides the connection's inbox under the hood),
/// but subscribing to the other principal's inbox prefix, or the broad account-wide
/// <c>_INBOX.&gt;</c>, is denied by the server.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ClientLibraryInboxIsolationTests(IntegrationEnvironment environment) : IAsyncLifetime
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
    public async Task EachConnection_CanUseItsOwnInbox_ViaKvRequestReply()
    {
        // Two distinct principals: the notes editor (password grant) and the tasks service
        // (client credentials), each granted their own scoped inbox prefix by the library.
        await using var notesConnection = await ConnectNotesEditorAsync();
        await using var tasksConnection = await ConnectTasksServiceAsync();

        // A JetStream KV read is a real request/reply round-trip that depends entirely on the
        // connection's own inbox subscription succeeding — exactly what the scoped inbox grant
        // exists to allow (doc "Fixtures": KV client subjects are part of every authenticated
        // grant). A failure here would surface as a hung GetEntryAsync, not a thrown exception.
        var notesKeysStore = await notesConnection.Connection!.CreateKeyValueStoreContext().GetStoreAsync("callout_assertion_keys").AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        var notesCurrentKid = await notesKeysStore.GetEntryAsync<string>("current").AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(notesCurrentKid.Value);

        var tasksKeysStore = await tasksConnection.Connection!.CreateKeyValueStoreContext().GetStoreAsync("callout_assertion_keys").AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        var tasksCurrentKid = await tasksKeysStore.GetEntryAsync<string>("current").AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(tasksCurrentKid.Value);
    }

    [Fact]
    public async Task SubscribingToAnotherPrincipalsInboxPrefix_IsDenied()
    {
        await using var notesConnection = await ConnectNotesEditorAsync();
        await using var tasksConnection = await ConnectTasksServiceAsync();

        // The tasks-service connection's own prefix is granted; the notes editor's is not.
        var foreignPrefix = AuthCallout.Contracts.Config.AppSlug
            .From(TestApplications.NotesAppName)
            .ClientInboxSubscribeSubject(await ReadPrincipalAsync(IntegrationRealm.NotesClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword));

        await AssertSubscribeDeniedAsync(tasksConnection.Connection!, foreignPrefix);
    }

    [Fact]
    public async Task SubscribingToTheAccountWideInboxWildcard_IsDenied()
    {
        await using var notesConnection = await ConnectNotesEditorAsync();

        await AssertSubscribeDeniedAsync(notesConnection.Connection!, "_INBOX.>");
    }

    private async Task<string> ReadPrincipalAsync(string clientId, string username, string password)
    {
        var token = await environment.Keycloak.GetPasswordGrantTokenAsync(clientId, username, password);
        return JwtInspector.ReadClaim(token, "sub");
    }

    private async Task<CalloutNatsConnection> ConnectNotesEditorAsync()
    {
        var token = await environment.Keycloak.GetPasswordGrantTokenAsync(
            IntegrationRealm.NotesClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);
        var connection = new CalloutNatsConnection(new CalloutClientOptions
        {
            Url = environment.Nats.ConnectionUrl,
            ApplicationName = TestApplications.NotesAppName,
            TokenProvider = new StaticTokenProvider(token),
            Name = "inbox-isolation-notes",
        });
        await connection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(20));
        return connection;
    }

    private async Task<CalloutNatsConnection> ConnectTasksServiceAsync()
    {
        var keycloak = environment.Keycloak;
        var token = await keycloak.GetClientCredentialsTokenAsync(IntegrationRealm.ServiceClientId, IntegrationRealm.ServiceClientSecret);
        var connection = new CalloutNatsConnection(new CalloutClientOptions
        {
            Url = environment.Nats.ConnectionUrl,
            ApplicationName = TestApplications.TasksAppName,
            TokenProvider = new StaticTokenProvider(token),
            Name = "inbox-isolation-tasks",
        });
        await connection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(20));
        return connection;
    }

    private static async Task AssertSubscribeDeniedAsync(NatsConnection client, string subject)
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
            var enumerator = client.SubscribeAsync<byte[]>(subject).GetAsyncEnumerator();
            var moveNextTask = enumerator.MoveNextAsync().AsTask();

            for (var attempt = 0; attempt < 20 && !violation; attempt++)
            {
                await Task.Delay(100);
                await client.PingAsync();
            }

            Assert.True(violation, $"Expected subscribing to '{subject}' to be denied with a permissions/authorization violation.");

            // The denied subscription never yields a message; abandon the pending read rather
            // than waiting on it (it would hang until the connection is disposed).
            _ = moveNextTask;
        }
        finally
        {
            client.ServerError -= OnError;
        }
    }
}
