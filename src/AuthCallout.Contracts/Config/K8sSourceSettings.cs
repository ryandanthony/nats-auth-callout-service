// -----------------------------------------------------------------------
// <copyright file="K8sSourceSettings.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Config;

/// <summary>Settings for the Kubernetes CRD source (doc 04, doc 09 §4, P11).</summary>
/// <param name="OwnerLabelSelector">The label selector this instance uses to claim the Custom Resources it owns, cluster-wide (ADR 0005).</param>
#pragma warning disable S101 // "K8s" matches the repo-wide convention (AuthCallout.Config.K8s project, SourceKind.K8s, doc 04/09 terminology); "K8S" would be inconsistent with every other K8s-named symbol.
public sealed record K8sSourceSettings(string OwnerLabelSelector);
#pragma warning restore S101
