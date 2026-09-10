// -----------------------------------------------------------------------
// <copyright file="ServiceStateChangedEventArgs.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;

namespace AuthCallout.Host.Lifecycle;

/// <summary>Event data for <see cref="ServiceStateMachine.StateChanged"/>.</summary>
public sealed class ServiceStateChangedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="ServiceStateChangedEventArgs"/> class.</summary>
    /// <param name="previous">The state transitioned from.</param>
    /// <param name="next">The state transitioned to.</param>
    public ServiceStateChangedEventArgs(ServiceState previous, ServiceState next)
    {
        Previous = previous;
        Next = next;
    }

    /// <summary>Gets the state transitioned from.</summary>
    /// <value>The previous state.</value>
    public ServiceState Previous { get; }

    /// <summary>Gets the state transitioned to.</summary>
    /// <value>The next state.</value>
    public ServiceState Next { get; }
}
