// -----------------------------------------------------------------------
// <copyright file="StatusPatchRecord.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Config.K8s.Client;

namespace AuthCallout.Config.Tests.K8s;

/// <summary>One status patch call recorded by <see cref="FakeCrdKubernetesClient"/>.</summary>
/// <param name="Resource">Which CR the patch targeted.</param>
/// <param name="ObservedGeneration">The generation the patch reported.</param>
/// <param name="Accepted">Whether the patch reported acceptance.</param>
/// <param name="Reason">The patch's reason text.</param>
public sealed record StatusPatchRecord(CustomResourceRecord Resource, long ObservedGeneration, bool Accepted, string Reason);
