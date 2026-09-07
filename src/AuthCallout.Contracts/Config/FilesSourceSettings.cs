// -----------------------------------------------------------------------
// <copyright file="FilesSourceSettings.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Config;

/// <summary>Settings for the files source (doc 04, doc 09 §3, P10).</summary>
/// <param name="Directory">The directory of per-application files, scanned non-recursively.</param>
public sealed record FilesSourceSettings(string Directory);
