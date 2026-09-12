// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Dev.Samples.Common;
using AuthCallout.Dev.Samples.NoteService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using NATS.Net;
using Wolverine;
using Wolverine.Nats;

// NoteService (Wolverine edition): the "notes" application as a Wolverine app over NATS,
// authenticated through the callout. This one process hosts both the service (domain handlers
// + in-memory store) and a demo client (DemoClient) that drives traffic, so the sample stays
// a single `dotnet run` while still crossing NATS for real.
//
// Startup sequence:
//   1. Fetch an OIDC token for the notes dev user and connect through the callout (raw NATS
//      connection) to read the app's discovery descriptor + this connection's own signed
//      identity assertion (doc 06). That connection + descriptor back the service-side
//      CalloutIdentityResolver/Verifier the middleware uses.
//   2. Stand up Wolverine on NATS with the same token, listening on notes.commands/queries
//      and publishing events to notes.events, with CalloutIdentityMiddleware enforcing the
//      per-message Callout-Identity check on every client request.

const string AppSlug = "notes";

using var startupCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

// Step 1: callout-authorized raw connection for the KV/identity plumbing.
var connection = await CalloutConnection.PrepareAsync(
    AppSlug, clientId: "notes-web", username: "note-user", password: "note-pass", startupCts.Token);

await using var natsConnection = new NatsConnection(connection.Options);
await natsConnection.ConnectAsync();
var kv = natsConnection.CreateKeyValueStoreContext();
var descriptor = await CalloutDiscovery.FetchAsync(kv, AppSlug);
var ownAssertion = await CalloutSelfAssertion.FetchAsync(kv, descriptor, connection.Token);
var resolver = new CalloutIdentityResolver(kv, descriptor);

// Step 2: the Wolverine host.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<NoteStore>();
builder.Services.AddSingleton(descriptor);
builder.Services.AddSingleton<CalloutIdentityResolver>(resolver);
builder.Services.AddSingleton<ICalloutIdentityVerifier, CalloutIdentityVerifier>();
builder.Services.AddSingleton(new NoteClientAssertion(ownAssertion));
builder.Services.AddHostedService<DemoClient>();

builder.UseWolverine(opts =>
{
    opts.UseNats(nats =>
    {
        nats.ConnectionString = connection.Options.Url!;
        nats.Token = connection.Token;
    });

    // Every client-originated command/query is identity-checked before its handler runs
    // (internal domain events are exempt — they carry no Callout-Identity header).
    opts.Policies.ForMessagesOfType<IClientRequest>().AddMiddleware(typeof(CalloutIdentityMiddleware));

    // Commands and queries the client sends; events the service publishes to itself. All
    // stay under the notes.> subject space the callout grant authorizes for this app.
    opts.PublishMessage<CreateNote>().ToNatsSubject("notes.commands");
    opts.PublishMessage<RenameNote>().ToNatsSubject("notes.commands");
    opts.PublishMessage<GetNote>().ToNatsSubject("notes.queries");
    opts.PublishMessage<ListNotes>().ToNatsSubject("notes.queries");
    opts.PublishMessage<NoteCreated>().ToNatsSubject("notes.events");
    opts.PublishMessage<NoteRenamed>().ToNatsSubject("notes.events");

    opts.ListenToNatsSubject("notes.commands");
    opts.ListenToNatsSubject("notes.queries");
    opts.ListenToNatsSubject("notes.events");
});

await builder.Build().RunAsync();
