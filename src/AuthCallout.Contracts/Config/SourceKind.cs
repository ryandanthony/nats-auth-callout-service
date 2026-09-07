// -----------------------------------------------------------------------
// <copyright file="SourceKind.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Config;

/// <summary>The routing/apps provider kinds (ADR 0005, QUESTIONS A2 for Postgres).</summary>
public enum SourceKind
{
    /// <summary>A directory of per-application JSON/YAML files, watched for change.</summary>
    Files,

    /// <summary>A Kubernetes CRD, one Custom Resource per application, watched cluster-wide.</summary>
    K8s,

    /// <summary>PostgreSQL rows, one document per application, polled and/or LISTEN/NOTIFY-driven.</summary>
    Postgres,
}
