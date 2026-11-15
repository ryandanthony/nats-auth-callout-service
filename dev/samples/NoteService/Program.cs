// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Tokens;
using AuthCallout.Client.Wolverine;
using AuthCallout.Dev.Samples.Common;
using AuthCallout.Dev.Samples.NoteService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Nats;

// NoteService (Wolverine edition, now on AuthCallout.Client.Wolverine): the "notes"
// application as a Wolverine app over NATS, authenticated through the callout. This one
// process hosts both the service (domain handlers + in-memory store) and a demo client
// (DemoClient) that drives traffic, so the sample stays a single `dotnet run` while still
// crossing NATS for real.
//
// All callout plumbing - token fetch, scoped connection, discovery, outbound identity-header
// attach, inbound verification - lives in UseNatsWithCallout + the package's default
// identity-verification policy; this sample only wires the domain.
//
// Token provider: the dev realm's note-user is a human-shaped Resource Owner Password
// Credentials login (DevOidcTokenFetcher), not a service account, so this wraps that fetch in
// a DelegateTokenProvider rather than AuthCallout.Client's client-credentials provider (which
// models ADR 0002's service-to-service grant, the shape a real Wolverine *service* host would
// use - see TaskService's DemoClient doc comment for the same note).

const string AppName = "notes";

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<NoteStore>();
builder.Services.AddHostedService<DemoClient>();

builder.UseWolverine(opts =>
{
    opts.UseNatsWithCallout(callout =>
    {
        callout.Url = DevEnvironment.ReadNatsUrl();
        callout.ApplicationName = AppName;
        callout.TokenProvider = new DelegateTokenProvider(cancellationToken =>
            FetchTokenAsync(cancellationToken));
    });

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

// Fetches a fresh token for the dev realm's note-user on every call (no caching here: the
// managed Wolverine transport connection calls this on every (re)connect, and
// DevOidcTokenFetcher's own Keycloak round trip is cheap enough for a dev sample).
static async ValueTask<string?> FetchTokenAsync(CancellationToken cancellationToken)
{
    var token = await DevOidcTokenFetcher.FetchAccessTokenAsync(
        DevEnvironment.ReadKeycloakBaseUrl(), realm: "dev", clientId: "notes-web", username: "note-user", password: "note-pass", cancellationToken);
    return token;
}
