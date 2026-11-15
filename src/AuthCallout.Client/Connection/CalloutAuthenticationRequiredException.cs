// -----------------------------------------------------------------------
// <copyright file="CalloutAuthenticationRequiredException.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Client.Connection;

/// <summary>
/// Thrown when the authenticated connect path has no token available (ADR 0021 rule 3: the
/// token provider returned null). A distinct, terminal failure — the managed connection never
/// downgrades this to an anonymous connect; the caller must obtain a token (e.g. "re-login
/// needed") and retry explicitly.
/// </summary>
public sealed class CalloutAuthenticationRequiredException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CalloutAuthenticationRequiredException"/> class.</summary>
    public CalloutAuthenticationRequiredException()
        : base("No token is available from the configured token provider; the authenticated connect path cannot proceed (ADR 0021 rule 3: never downgrade to anonymous).")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CalloutAuthenticationRequiredException"/> class.</summary>
    /// <param name="message">The exception message.</param>
    public CalloutAuthenticationRequiredException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CalloutAuthenticationRequiredException"/> class.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public CalloutAuthenticationRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
