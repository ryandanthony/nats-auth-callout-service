// -----------------------------------------------------------------------
// <copyright file="TaskServiceRunner.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Dev.Samples.Common;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;

namespace AuthCallout.Dev.Samples.TaskService;

/// <summary>
/// TaskService (P15 sample, doc 08 §Local development "Task service"): a second,
/// independent application on <c>tasks.&gt;</c>, a different issuer <c>match</c>
/// (<c>azp: [tasks-web]</c> vs. NoteService's <c>notes-web</c>), and a "worker" service
/// profile with <c>autoAddSessionAccess</c> to resolve callers, exactly like NoteService's
/// subscriber but for a distinct application. Running both samples together against the
/// same callout instance proves two things end to end:
/// <list type="bullet">
/// <item><description><b>Isolation (ADR 0001).</b> This application's token is minted into
/// the <c>TASKS</c> NATS account, a token valid for <c>notes</c> never yields
/// <c>tasks.&gt;</c> subjects and vice versa — the two samples' grants never
/// overlap.</description></item>
/// <item><description><b>Anonymous union across apps (ADR 0004).</b> This application
/// contributes <c>anonymousProfile.subscribe: [tasks.public.&gt;]</c> to the global
/// anonymous grant, alongside NoteService's own <c>notes.public.&gt;</c> contribution — an
/// anonymous connection (no token) can subscribe to <i>both</i>, proving the union is
/// computed across every configured application, not scoped to one.</description></item>
/// </list>
/// Otherwise this sample is deliberately the same shape as NoteService (connect, discover,
/// publish/subscribe a couple of subjects, resolve identity) — see
/// <see cref="AuthCallout.Dev.Samples.NoteService.NoteServiceRunner"/> for the shared
/// identity-propagation walk-through this sample also exercises.
/// </summary>
public static class TaskServiceRunner
{
    /// <summary>Gets the well-known application slug this sample authorizes against (doc 09 <c>name: tasks</c>).</summary>
    /// <value>The application slug, <c>tasks</c>.</value>
    public static string AppSlugText => "tasks";

    /// <summary>Gets the subject this sample publishes/subscribes on (matches <c>Task.yaml</c>'s <c>tasks.dev.&gt;</c> grant).</summary>
    /// <value>The demo subject, <c>tasks.dev.hello</c>.</value>
    public static string Subject => "tasks.dev.hello";

    /// <summary>Gets the number of demo tasks the publisher sends per run.</summary>
    /// <value>The publish count, <see cref="DemoTaskCount"/>.</value>
#pragma warning disable S109 // This is the named constant itself; nothing to extract it from.
    public static int PublishCount => DemoTaskCount;
#pragma warning restore S109

    private const int DemoTaskCount = 3;

    /// <summary>Runs the sample to completion: connect, discover, publish a few tasks, observe them as a subscriber, disconnect.</summary>
    /// <returns>A task that completes when the demo run finishes.</returns>
    public static async Task RunAsync()
    {
        var natsUrl = DevEnvironment.ReadNatsUrl();
        var keycloakBaseUrl = DevEnvironment.ReadKeycloakBaseUrl();
        var token = await DevOidcTokenFetcher.FetchAccessTokenAsync(
            keycloakBaseUrl, realm: "dev", clientId: "tasks-web", username: "task-user", password: "task-pass", CancellationToken.None);

        Console.WriteLine($"[task] connecting to {natsUrl} with an OIDC bearer token...");
        await using var client = new NatsClient(new NatsOpts
        {
            Url = natsUrl,
            Name = "task-sample",
            AuthOpts = new NatsAuthOpts { Token = token },

            // The callout grants a scoped inbox (_INBOX.{slug}.{principal-key}.>), not the
            // account-wide _INBOX.>; the connection must use the matching prefix or KV reads
            // (and any request-reply) would be silently denied their reply subscription.
            InboxPrefix = CalloutInboxPrefix.For(AppSlugText, token),
        });
        await client.ConnectAsync();
        Console.WriteLine("[task] connected and authorized (callout granted a TASKS-account user JWT, isolated from NOTES - ADR 0001).");

        var kv = client.CreateKeyValueStoreContext();
        var descriptor = await CalloutDiscovery.FetchAsync(kv, AppSlugText);
        Console.WriteLine($"[task] discovery: identityPropagation.enabled={descriptor.IdentityPropagation.Enabled}");

        var assertion = descriptor.IdentityPropagation.Enabled
            ? await CalloutSelfAssertion.FetchAsync(kv, descriptor, token)
            : null;

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var subscriberReady = new TaskCompletionSource();
        var subscriberTask = Task.Run(() => RunSubscriberAsync(client, kv, descriptor, subscriberReady, runCts.Token), runCts.Token);
        await subscriberReady.Task.WaitAsync(runCts.Token);

        await PublishDemoTasksAsync(client, descriptor, assertion, runCts.Token);

        await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
        await runCts.CancelAsync();
        try
        {
            await subscriberTask;
        }
        catch (OperationCanceledException)
        {
            // Expected: the subscriber loop is cancelled once the demo publishes are done.
        }

        Console.WriteLine("[task] done.");
    }

    private static async Task PublishDemoTasksAsync(NatsClient client, DiscoveryDescriptor descriptor, string? assertionCompactJwt, CancellationToken cancellationToken)
    {
        var headerName = descriptor.IdentityPropagation.Header ?? CalloutIdentityHeader.FallbackName;
        for (var i = 1; i <= PublishCount; i++)
        {
            var headers = assertionCompactJwt is not null ? new NatsHeaders { { headerName, assertionCompactJwt } } : null;
            await client.Connection.PublishAsync(Subject, $"task #{i} from the sample publisher", headers: headers, cancellationToken: cancellationToken);
            Console.WriteLine($"[task] published task #{i}" + (headers is null ? " (no identity header)" : " with Callout-Identity header"));
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static async Task RunSubscriberAsync(
        NatsClient client, INatsKVContext kv, DiscoveryDescriptor descriptor, TaskCompletionSource ready, CancellationToken cancellationToken)
    {
        var resolver = new CalloutIdentityResolver(kv, descriptor);
        var headerName = descriptor.IdentityPropagation.Header ?? CalloutIdentityHeader.FallbackName;

        var subscription = client.Connection.SubscribeAsync<string>(Subject, cancellationToken: cancellationToken);
        await using var enumerator = subscription.GetAsyncEnumerator(cancellationToken);
        ready.TrySetResult();

        while (await enumerator.MoveNextAsync())
        {
            var msg = enumerator.Current;
            Console.WriteLine($"[task] subscriber received: \"{msg.Data}\"");

            if (!descriptor.IdentityPropagation.Enabled || msg.Headers is null || !msg.Headers.TryGetValue(headerName, out var assertionValues))
            {
                Console.WriteLine("[task] subscriber: no identity header present.");
                continue;
            }

            var verified = await resolver.VerifyAsync(assertionValues.ToString());
            if (verified is null)
            {
                Console.WriteLine("[task] subscriber: identity assertion failed verification.");
                continue;
            }

            Console.WriteLine($"[task] subscriber: verified identity id={verified.Id} sub={verified.Subject} app={verified.Application}");

            var rawToken = await resolver.ResolveOidcTokenAsync(verified.Id);
            var preferredUsername = OidcTokenClaims.ReadClaim(rawToken, "preferred_username");
            Console.WriteLine($"[task] subscriber: resolved caller's OIDC token, preferred_username={preferredUsername}");
        }
    }
}
