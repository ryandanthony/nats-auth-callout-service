// -----------------------------------------------------------------------
// <copyright file="CrdWatchGoneException.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Config.K8s.Client;

/// <summary>
/// Thrown by <see cref="ICrdKubernetesClient.WatchAsync"/> when the API server responds
/// <c>410 Gone</c> (the watch's <c>resourceVersion</c> expired) — the documented signal to
/// relist rather than reconnect the same watch (doc 08 §Kubernetes CRD source, ADR 0017).
/// </summary>
public sealed class CrdWatchGoneException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CrdWatchGoneException"/> class.</summary>
    public CrdWatchGoneException()
        : base("The Custom Resource watch's resourceVersion expired (410 Gone); a relist is required.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CrdWatchGoneException"/> class with a custom message.</summary>
    /// <param name="message">The exception message.</param>
    public CrdWatchGoneException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CrdWatchGoneException"/> class with a custom message and inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public CrdWatchGoneException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
