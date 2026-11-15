// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityVerifier.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;

namespace AuthCallout.Client.Identity;

/// <summary>
/// The production-shaped <see cref="ICalloutIdentityVerifier"/>: delegates to a
/// <see cref="CalloutIdentityResolver"/> bound to one application's discovery descriptor and
/// the service's own KV context. Registered as a singleton by
/// <c>ServiceCollectionExtensions.AddCalloutNatsClient</c> so any message-handling middleware
/// resolves it per message.
/// </summary>
public sealed class CalloutIdentityVerifier : ICalloutIdentityVerifier
{
    private readonly CalloutIdentityResolver _resolver;

    /// <summary>Initializes a new instance of the <see cref="CalloutIdentityVerifier"/> class.</summary>
    /// <param name="resolver">The resolver bound to this app's descriptor + KV context.</param>
    public CalloutIdentityVerifier(CalloutIdentityResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <inheritdoc />
    public Task<AssertionClaims?> VerifyAsync(string compactAssertionJwt, CancellationToken cancellationToken = default) =>
        _resolver.VerifyAsync(compactAssertionJwt, cancellationToken);
}
