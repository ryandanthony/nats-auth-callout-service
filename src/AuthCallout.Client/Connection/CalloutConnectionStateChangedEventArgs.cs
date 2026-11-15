// -----------------------------------------------------------------------
// <copyright file="CalloutConnectionStateChangedEventArgs.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Client.Connection;

/// <summary>Event args for <see cref="CalloutNatsConnection.StateChanged"/>.</summary>
public sealed class CalloutConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="CalloutConnectionStateChangedEventArgs"/> class.</summary>
    /// <param name="state">The new state.</param>
    public CalloutConnectionStateChangedEventArgs(CalloutConnectionState state)
    {
        State = state;
    }

    /// <summary>Gets the new state.</summary>
    /// <value>The new state.</value>
    public CalloutConnectionState State { get; }
}
