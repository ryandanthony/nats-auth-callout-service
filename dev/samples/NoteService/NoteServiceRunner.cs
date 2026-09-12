// -----------------------------------------------------------------------
// <copyright file="NoteServiceRunner.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Dev.Samples.Common;
using NATS.Client.Core;
using NATS.Net;

namespace AuthCallout.Dev.Samples.NoteService;

/// <summary>
/// NoteService (P15 sample, doc 08 §Local development "Note service"): connects to NATS with
/// an OIDC bearer token from the dev Keycloak realm, which drives it through the callout into
/// the "notes" application's "editor" profile. Demonstrates the full doc 06 identity-
/// propagation sequence end to end:
/// <list type="number">
/// <item><description>After connecting, fetches the "notes" discovery descriptor (once,
/// <see cref="CalloutDiscovery"/>) to learn whether propagation is enabled and, if so, the
/// header/bucket names + current <c>kid</c>.</description></item>
/// <item><description>Fetches its own signed assertion (keyed by its token's <c>jti</c>,
/// <see cref="CalloutSelfAssertion"/>) and caches it.</description></item>
/// <item><description>Attaches the assertion as the identity header on every publish to
/// <c>notes.dev.&gt;</c> (<see cref="NotePublisher"/>).</description></item>
/// <item><description>Runs a subscriber loop on the same subject
/// (<see cref="NoteSubscriber"/>) that verifies the identity header on incoming messages
/// (its own publishes included) via <see cref="AssertionVerification"/> and, since this
/// app's "editor" profile has <c>autoAddSessionAccess</c>, resolves the publisher's original
/// OIDC token from the token-store bucket and logs a claim from it.</description></item>
/// </list>
/// This is the manual end-to-end check doc 08 describes ("Both are thin: connect,
/// publish/subscribe a couple of subjects, and log the verdict... double as the manual
/// end-to-end check and the seed for the integration tests").
/// </summary>
public static class NoteServiceRunner
{
    /// <summary>Gets the well-known application slug this sample authorizes against (doc 09 <c>name: notes</c>).</summary>
    /// <value>The application slug, <c>notes</c>.</value>
    public static string AppSlugText => "notes";

    /// <summary>Gets the subject this sample publishes/subscribes on (matches <c>Note.yaml</c>'s <c>notes.dev.&gt;</c> grant).</summary>
    /// <value>The demo subject, <c>notes.dev.hello</c>.</value>
    public static string Subject => "notes.dev.hello";

    /// <summary>Gets the number of demo notes the publisher sends per run.</summary>
    /// <value>The publish count, <see cref="DemoNoteCount"/>.</value>
#pragma warning disable S109 // This is the named constant itself; nothing to extract it from.
    public static int PublishCount => DemoNoteCount;
#pragma warning restore S109

    private const int DemoNoteCount = 3;

    /// <summary>Runs the sample to completion: connect, discover, publish a few notes, observe them as a subscriber, disconnect.</summary>
    /// <returns>A task that completes when the demo run finishes.</returns>
    public static async Task RunAsync()
    {
        var natsUrl = DevEnvironment.ReadNatsUrl();
        var keycloakBaseUrl = DevEnvironment.ReadKeycloakBaseUrl();
        var token = await DevOidcTokenFetcher.FetchAccessTokenAsync(
            keycloakBaseUrl, realm: "dev", clientId: "notes-web", username: "note-user", password: "note-pass", CancellationToken.None);

        Console.WriteLine($"[note] connecting to {natsUrl} with an OIDC bearer token...");
        await using var client = new NatsClient(new NatsOpts
        {
            Url = natsUrl,
            Name = "note-sample",
            AuthOpts = new NatsAuthOpts { Token = token },

            // The callout grants a scoped inbox (_INBOX.{slug}.{principal-key}.>), not the
            // account-wide _INBOX.>; the connection must use the matching prefix or KV reads
            // (and any request-reply) would be silently denied their reply subscription.
            InboxPrefix = CalloutInboxPrefix.For(AppSlugText, token),
        });
        await client.ConnectAsync();
        Console.WriteLine("[note] connected and authorized (callout granted a NOTES-account user JWT).");

        var kv = client.CreateKeyValueStoreContext();
        var descriptor = await CalloutDiscovery.FetchAsync(kv, AppSlugText);
        Console.WriteLine($"[note] discovery: identityPropagation.enabled={descriptor.IdentityPropagation.Enabled}");

        var assertion = descriptor.IdentityPropagation.Enabled
            ? await CalloutSelfAssertion.FetchAsync(kv, descriptor, token)
            : null;
        if (assertion is not null)
        {
            Console.WriteLine("[note] fetched own signed identity assertion (kid header carried inside it).");
        }

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var subscriberReady = new TaskCompletionSource();
        var subscriberTask = Task.Run(() => NoteSubscriber.RunAsync(client, kv, descriptor, subscriberReady, runCts.Token), runCts.Token);
        await subscriberReady.Task.WaitAsync(runCts.Token);

        await NotePublisher.PublishDemoNotesAsync(client, descriptor, assertion, runCts.Token);

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

        Console.WriteLine("[note] done.");
    }
}
