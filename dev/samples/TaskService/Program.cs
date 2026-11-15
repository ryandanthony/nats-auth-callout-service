// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Tokens;
using AuthCallout.Client.Wolverine;
using AuthCallout.Dev.Samples.Common;
using AuthCallout.Dev.Samples.TaskService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Nats;

// TaskService (Wolverine edition, on AuthCallout.Client.Wolverine): the "tasks" application,
// structurally identical to NoteService but a fully independent app - its own dev user/client,
// NATS account (TASKS), and tasks.> subject space. Running both against the same callout
// proves application isolation (ADR 0001): the tasks token never yields notes.> subjects and
// vice versa. See NoteService/Program.cs for the annotated startup sequence and the token
// provider note (dev realm user login, not a service account).

const string AppName = "tasks";

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<TaskStore>();
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

// Fetches a fresh token for the dev realm's task-user on every call (see NoteService's
// Program.cs for why this is a DelegateTokenProvider rather than client-credentials).
static async ValueTask<string?> FetchTokenAsync(CancellationToken cancellationToken)
{
    var token = await DevOidcTokenFetcher.FetchAccessTokenAsync(
        DevEnvironment.ReadKeycloakBaseUrl(), realm: "dev", clientId: "tasks-web", username: "task-user", password: "task-pass", cancellationToken);
    return token;
}
