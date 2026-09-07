// -----------------------------------------------------------------------
// <copyright file="ActiveConfigChangedEventArgs.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// Event data for <see cref="IActiveConfigSource.Changed"/>: the newly-applied snapshot.
/// </summary>
public sealed class ActiveConfigChangedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="ActiveConfigChangedEventArgs"/> class, wrapping the newly-applied snapshot.</summary>
    /// <param name="newConfig">The snapshot that just became active.</param>
    public ActiveConfigChangedEventArgs(ActiveConfig newConfig)
    {
        ArgumentNullException.ThrowIfNull(newConfig);
        NewConfig = newConfig;
    }

    /// <summary>Gets the snapshot that just became <see cref="IActiveConfigSource.Current"/>.</summary>
    /// <value>The newly-applied active config snapshot.</value>
    public ActiveConfig NewConfig { get; }
}
