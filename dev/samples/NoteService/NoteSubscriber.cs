// -----------------------------------------------------------------------
// <copyright file="NoteSubscriber.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Dev.Samples.Common;
using NATS.Client.KeyValueStore;
using NATS.Net;

namespace AuthCallout.Dev.Samples.NoteService;

/// <summary>Subscribes to the demo subject and resolves each message's verified caller identity (doc 06).</summary>
public static class NoteSubscriber
{
    /// <summary>
    /// Runs the subscribe loop until <paramref name="cancellationToken"/> fires, logging each
    /// message's verified identity (or lack of one) and, when resolvable, the caller's
    /// <c>preferred_username</c> claim from its stored OIDC token.
    /// </summary>
    /// <param name="client">The connected NATS client.</param>
    /// <param name="kv">The client's KV context.</param>
    /// <param name="descriptor">The application's discovery descriptor.</param>
    /// <param name="ready">Signaled once the subscription is established, so the caller can start publishing.</param>
    /// <param name="cancellationToken">Cancellation for the subscribe loop.</param>
    /// <returns>A task that completes when the loop is cancelled.</returns>
    public static async Task RunAsync(
        NatsClient client, INatsKVContext kv, DiscoveryDescriptor descriptor, TaskCompletionSource ready, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(ready);

        var resolver = new CalloutIdentityResolver(kv, descriptor);
        var headerName = descriptor.IdentityPropagation.Header ?? CalloutIdentityHeader.FallbackName;

        var subscription = client.Connection.SubscribeAsync<string>(NoteServiceRunner.Subject, cancellationToken: cancellationToken);
        await using var enumerator = subscription.GetAsyncEnumerator(cancellationToken);
        ready.TrySetResult();

        while (await enumerator.MoveNextAsync())
        {
            var msg = enumerator.Current;
            Console.WriteLine($"[note] subscriber received: \"{msg.Data}\"");

            if (!descriptor.IdentityPropagation.Enabled || msg.Headers is null || !msg.Headers.TryGetValue(headerName, out var assertionValues))
            {
                Console.WriteLine("[note] subscriber: no identity header present (propagation disabled or header missing).");
                continue;
            }

            var verified = await resolver.VerifyAsync(assertionValues.ToString()).ConfigureAwait(false);
            if (verified is null)
            {
                Console.WriteLine("[note] subscriber: identity assertion failed verification; treating message as unverified.");
                continue;
            }

            Console.WriteLine($"[note] subscriber: verified identity id={verified.Id} sub={verified.Subject}");

            var rawToken = await resolver.ResolveOidcTokenAsync(verified.Id).ConfigureAwait(false);
            var preferredUsername = OidcTokenClaims.ReadClaim(rawToken, "preferred_username");
            Console.WriteLine($"[note] subscriber: resolved caller's OIDC token, preferred_username={preferredUsername}");
        }
    }
}
