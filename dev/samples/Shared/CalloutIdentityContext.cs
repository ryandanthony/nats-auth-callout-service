// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityContext.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// The verified caller identity a <see cref="CalloutIdentityMiddleware"/> extracts from an
/// inbound message's <c>Callout-Identity</c> header and makes available to Wolverine handlers
/// (Wolverine resolves a middleware's non-<see cref="Wolverine.HandlerContinuation"/> return
/// value as a per-message service, so a handler can simply take a
/// <see cref="CalloutIdentityContext"/> parameter). Holds the assertion claims the callout
/// signed (ADR 0011): the connection <c>id</c>, the caller's <c>sub</c>, the application, and
/// the issuer. Only ever constructed after signature + expiry verification succeeds.
/// </summary>
public sealed class CalloutIdentityContext
{
    /// <summary>Initializes a new instance of the <see cref="CalloutIdentityContext"/> class.</summary>
    /// <param name="claims">The verified assertion claims.</param>
    public CalloutIdentityContext(AssertionClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        Claims = claims;
    }

    /// <summary>Gets the verified assertion claims (callout-generated <c>id</c>, caller <c>sub</c>, <c>app</c>, <c>iss</c>, <c>exp</c>).</summary>
    public AssertionClaims Claims { get; }

    /// <summary>Gets the caller's subject (<c>sub</c>) from the verified assertion.</summary>
    public string Subject => Claims.Subject;

    /// <summary>Gets the callout-generated connection id (<c>id</c>) that keys the caller's stored OIDC token.</summary>
    public string ConnectionId => Claims.Id;

    /// <summary>Gets the application slug the assertion was scoped to (<c>app</c>).</summary>
    public string Application => Claims.Application;
}
