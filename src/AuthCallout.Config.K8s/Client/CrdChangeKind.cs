// -----------------------------------------------------------------------
// <copyright file="CrdChangeKind.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Config.K8s.Client;

/// <summary>
/// The kind of change a watch event reports for one Custom Resource. Public so
/// <c>AuthCallout.Config.Tests</c>' fake <see cref="ICrdKubernetesClient"/> can construct
/// watch events (P11 packet: "unit-test the provider against a faked watch-event stream").
/// </summary>
public enum CrdChangeKind
{
    /// <summary>The Custom Resource was created.</summary>
    Added,

    /// <summary>The Custom Resource was updated.</summary>
    Modified,

    /// <summary>The Custom Resource was deleted.</summary>
    Deleted,
}
