// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Dev.Samples.Common;
using AuthCallout.Dev.Samples.TaskService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using NATS.Net;
using Wolverine;
using Wolverine.Nats;

// TaskService (Wolverine edition): the "tasks" application, structurally identical to
// NoteService but a fully independent app — its own dev user/client, NATS account (TASKS),
// and tasks.> subject space. Running both against the same callout proves application
// isolation (ADR 0001): the tasks token never yields notes.> subjects and vice versa.
// See NoteService/Program.cs for the annotated startup sequence.

const string AppSlug = "tasks";

using var startupCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

var connection = await CalloutConnection.PrepareAsync(
    AppSlug, clientId: "tasks-web", username: "task-user", password: "task-pass", startupCts.Token);

await using var natsConnection = new NatsConnection(connection.Options);
await natsConnection.ConnectAsync();
var kv = natsConnection.CreateKeyValueStoreContext();
var descriptor = await CalloutDiscovery.FetchAsync(kv, AppSlug);
var ownAssertion = await CalloutSelfAssertion.FetchAsync(kv, descriptor, connection.Token);
var resolver = new CalloutIdentityResolver(kv, descriptor);

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<TaskStore>();
builder.Services.AddSingleton(descriptor);
builder.Services.AddSingleton<CalloutIdentityResolver>(resolver);
builder.Services.AddSingleton<ICalloutIdentityVerifier, CalloutIdentityVerifier>();
builder.Services.AddSingleton(new TaskClientAssertion(ownAssertion));
builder.Services.AddHostedService<DemoClient>();

builder.UseWolverine(opts =>
{
    opts.UseNats(nats =>
    {
        nats.ConnectionString = connection.Options.Url!;
        nats.Token = connection.Token;
    });

    opts.Policies.ForMessagesOfType<IClientRequest>().AddMiddleware(typeof(CalloutIdentityMiddleware));

    opts.PublishMessage<CreateTask>().ToNatsSubject("tasks.commands");
    opts.PublishMessage<CompleteTask>().ToNatsSubject("tasks.commands");
    opts.PublishMessage<GetTask>().ToNatsSubject("tasks.queries");
    opts.PublishMessage<GetOpenTasks>().ToNatsSubject("tasks.queries");
    opts.PublishMessage<TaskCreated>().ToNatsSubject("tasks.events");
    opts.PublishMessage<TaskCompleted>().ToNatsSubject("tasks.events");

    opts.ListenToNatsSubject("tasks.commands");
    opts.ListenToNatsSubject("tasks.queries");
    opts.ListenToNatsSubject("tasks.events");
});

await builder.Build().RunAsync();
