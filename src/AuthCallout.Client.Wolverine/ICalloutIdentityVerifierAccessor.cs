// -----------------------------------------------------------------------
// <copyright file="ICalloutIdentityVerifierAccessor.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Identity;

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// The DI-registered seam <see cref="CalloutIdentityMiddleware"/> depends on: the async
/// accessor to this host's <see cref="ICalloutIdentityVerifier"/> (building it, on first call,
/// from the KV connection <see cref="CalloutHostedConnection"/> establishes at startup — mirrors
/// <c>AuthCallout.Client.DependencyInjection.CalloutIdentityServices.GetIdentityVerifierAsync</c>,
/// which a synchronous DI factory cannot call directly without sync-over-async).
/// </summary>
public interface ICalloutIdentityVerifierAccessor
{
    /// <summary>Gets the identity verifier for this host's application, building it on first call.</summary>
    /// <param name="cancellationToken">Cancellation for the discovery fetch, on first call.</param>
    /// <returns>The identity verifier.</returns>
    Task<ICalloutIdentityVerifier> GetVerifierAsync(CancellationToken cancellationToken = default);
}
