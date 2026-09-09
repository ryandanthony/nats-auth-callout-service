// -----------------------------------------------------------------------
// <copyright file="CrdListResult.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Config.K8s.Client;

/// <summary>The result of a full list of owned Custom Resources (<see cref="ICrdKubernetesClient.ListAsync"/>).</summary>
/// <param name="Resources">Every owned Custom Resource at the time of the list.</param>
/// <param name="ResourceVersion">The list's resource version, the starting point for a subsequent <see cref="ICrdKubernetesClient.WatchAsync"/>.</param>
public sealed record CrdListResult(IReadOnlyList<CustomResourceRecord> Resources, string ResourceVersion);
