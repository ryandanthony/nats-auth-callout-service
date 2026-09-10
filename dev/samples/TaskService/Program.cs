// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Dev.Samples.TaskService;

/// <summary>Entry point: delegates to <see cref="TaskServiceRunner"/> (see its doc comment for what this sample demonstrates).</summary>
public static class Program
{
    /// <summary>Runs the TaskService sample to completion.</summary>
    /// <returns>A task that completes when the demo run finishes.</returns>
    public static Task Main() => TaskServiceRunner.RunAsync();
}
