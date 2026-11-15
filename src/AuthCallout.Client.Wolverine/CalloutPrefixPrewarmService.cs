// -----------------------------------------------------------------------
// <copyright file="CalloutPrefixPrewarmService.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using AuthCallout.Client.Tokens;
using AuthCallout.Contracts.Config;
using Microsoft.Extensions.Hosting;

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// Computes the scoped inbox prefix before Wolverine's transport connects. Wolverine's
/// <c>ConfigureNatsOpts</c> hook is synchronous and runs while the transport builds its
/// <c>NatsOpts</c> (during Wolverine's own hosted-service startup), so the async token fetch
/// the prefix depends on must have already happened. <see cref="NatsTransportExtensions.UseNatsWithCallout"/>
/// inserts this service at the <i>front</i> of the host's service collection — ahead of the
/// Wolverine runtime Wolverine registered before the configure callback ran — so hosted-service
/// start order guarantees the prefix is in <see cref="CalloutTransportContext.InboxPrefix"/>
/// by the time the hook reads it.
/// </summary>
/// <remarks>
/// The principal (token <c>sub</c>, falling back to <c>azp</c>) is assumed stable across
/// reconnects: for the client-credentials services this adapter targets (ADR 0002), the
/// principal is the service account, which never changes. A token whose principal changed
/// mid-lifetime would get a stale inbox prefix until host restart; Wolverine's own messaging
/// is unaffected either way (it never uses NATS.NET inboxes), only identity KV reads.
/// </remarks>
public sealed class CalloutPrefixPrewarmService : IHostedService
{
    private readonly CalloutWolverineOptions _options;
    private readonly CalloutTransportContext _context;

    /// <summary>Initializes a new instance of the <see cref="CalloutPrefixPrewarmService"/> class.</summary>
    /// <param name="options">The adapter options (token provider + application name).</param>
    /// <param name="context">The shared context the <c>ConfigureNatsOpts</c> hook reads the prefix from.</param>
    public CalloutPrefixPrewarmService(CalloutWolverineOptions options, CalloutTransportContext context)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);
        _options = options;
        _context = context;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var token = await _options.TokenProvider!.GetTokenAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new CalloutAuthenticationRequiredException();

        var principal = OidcTokenClaims.ReadSub(token) ?? OidcTokenClaims.ReadClaim(token, "azp")
            ?? throw new InvalidOperationException("Token has neither 'sub' nor 'azp'; cannot derive the scoped inbox prefix.");

        _context.InboxPrefix = AppSlug.From(_options.ApplicationName).ClientInboxPrefix(principal);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
