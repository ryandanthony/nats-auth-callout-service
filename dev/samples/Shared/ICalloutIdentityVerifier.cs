// -----------------------------------------------------------------------
// <copyright file="ICalloutIdentityVerifier.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// The DI-registered seam <see cref="CalloutIdentityMiddleware"/> depends on: verifies a
/// compact <c>Callout-Identity</c> assertion for one application and returns its claims, or
/// null when the assertion is missing, forged, unknown-key, or expired. Implemented by
/// <see cref="CalloutIdentityVerifier"/> over this app's discovery descriptor + KV context.
/// A separate interface (not the concrete <see cref="CalloutIdentityResolver"/>) so the
/// middleware stays a pure, testable function of the message plus this one dependency.
/// </summary>
public interface ICalloutIdentityVerifier
{
    /// <summary>Verifies a compact assertion JWT and returns its claims, or null when it cannot be trusted.</summary>
    /// <param name="compactAssertionJwt">The raw <c>Callout-Identity</c> header value.</param>
    /// <returns>The verified claims, or null.</returns>
    Task<AssertionClaims?> VerifyAsync(string compactAssertionJwt);
}
