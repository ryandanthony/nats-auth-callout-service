// -----------------------------------------------------------------------
// <copyright file="SecretResolutionException.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Secrets;

/// <summary>
/// Thrown by <see cref="SecretResolver"/> when a <c>file</c> or <c>k8sSecret</c>
/// <see cref="AuthCallout.Contracts.Primitives.SecretRef"/> could not be resolved (missing or
/// unreadable mount, doc 04 §Secrets, P03). The message always names the *locator* (path, or
/// Kubernetes Secret name/key) — never the secret value, which by definition was never
/// successfully read.
/// </summary>
public sealed class SecretResolutionException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="SecretResolutionException"/> class.</summary>
    public SecretResolutionException()
        : base("A secret reference could not be resolved.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SecretResolutionException"/> class with a custom message.</summary>
    /// <param name="message">The exception message, naming the locator (path/name/key), never the value.</param>
    public SecretResolutionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SecretResolutionException"/> class with a custom message and inner exception.</summary>
    /// <param name="message">The exception message, naming the locator (path/name/key), never the value.</param>
    /// <param name="innerException">The inner exception.</param>
    public SecretResolutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
