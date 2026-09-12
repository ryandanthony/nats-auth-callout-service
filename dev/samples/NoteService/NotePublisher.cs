// -----------------------------------------------------------------------
// <copyright file="NotePublisher.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Dev.Samples.Common;
using NATS.Client.Core;
using NATS.Net;

namespace AuthCallout.Dev.Samples.NoteService;

/// <summary>Publishes the demo notes, attaching the identity assertion header when propagation is enabled.</summary>
public static class NotePublisher
{
    /// <summary>Gets the delay between demo publishes, giving the subscriber loop time to log each one.</summary>
    /// <value>The inter-publish delay.</value>
    public static TimeSpan PublishDelay => TimeSpan.FromSeconds(1);

    /// <summary>Publishes <see cref="NoteServiceRunner.PublishCount"/> demo notes to <see cref="NoteServiceRunner.Subject"/>.</summary>
    /// <param name="client">The connected NATS client.</param>
    /// <param name="descriptor">The application's discovery descriptor.</param>
    /// <param name="assertionCompactJwt">This client's own signed assertion (null when propagation is disabled).</param>
    /// <param name="cancellationToken">Cancellation for the publish loop.</param>
    /// <returns>A task that completes once every demo note has been published.</returns>
    public static async Task PublishDemoNotesAsync(
        NatsClient client, DiscoveryDescriptor descriptor, string? assertionCompactJwt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(descriptor);

        var headerName = descriptor.IdentityPropagation.Header ?? CalloutIdentityHeader.FallbackName;
        for (var i = 1; i <= NoteServiceRunner.PublishCount; i++)
        {
            var headers = assertionCompactJwt is not null ? new NatsHeaders { { headerName, assertionCompactJwt } } : null;
            await client.Connection.PublishAsync(NoteServiceRunner.Subject, $"note #{i} from the sample publisher", headers: headers, cancellationToken: cancellationToken);
            Console.WriteLine($"[note] published note #{i}" + (headers is null ? " (no identity header, propagation disabled)" : " with Callout-Identity header"));
            await Task.Delay(PublishDelay, cancellationToken);
        }
    }
}
