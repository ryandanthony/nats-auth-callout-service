// -----------------------------------------------------------------------
// <copyright file="CrdChangeEvent.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Config.K8s.Client;

/// <summary>
/// One watch event for an owned <c>AuthApplication</c> Custom Resource
/// (<see cref="ICrdKubernetesClient.WatchAsync"/>).
/// </summary>
/// <param name="Kind">Whether the CR was added, modified, or deleted.</param>
/// <param name="Resource">
/// The current state of the CR. For <see cref="CrdChangeKind.Deleted"/>, only
/// <see cref="CustomResourceRecord.Name"/>/<see cref="CustomResourceRecord.Namespace"/>
/// are meaningful (used to remove it from the owned set); <see cref="CustomResourceRecord.Spec"/>
/// reflects the object's last-known state at deletion time and is not consulted.
/// </param>
public sealed record CrdChangeEvent(CrdChangeKind Kind, CustomResourceRecord Resource);
