// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Dev.Samples.NoteService;

/// <summary>Entry point: delegates to <see cref="NoteServiceRunner"/> (see its doc comment for what this sample demonstrates).</summary>
public static class Program
{
    /// <summary>Runs the NoteService sample to completion.</summary>
    /// <returns>A task that completes when the demo run finishes.</returns>
    public static Task Main() => NoteServiceRunner.RunAsync();
}
