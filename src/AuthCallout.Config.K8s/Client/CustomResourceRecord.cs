// -----------------------------------------------------------------------
// <copyright file="CustomResourceRecord.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;

namespace AuthCallout.Config.K8s.Client;

/// <summary>
/// A watched <c>AuthApplication</c> Custom Resource, reduced to what
/// <see cref="AuthCallout.Config.K8s.CrdRoutingConfigProvider"/> needs: identity (namespace
/// qualifies uniqueness even though <c>metadata.name</c> is the canonical application name,
/// doc 09 §4), the generation for status write-back (ADR 0006), and the raw <c>spec</c> for
/// <see cref="Mapping.CustomResourceSpecMapper"/>.
/// </summary>
/// <param name="Name">The CR's <c>metadata.name</c> (the application name, doc 09 §4).</param>
/// <param name="Namespace">The CR's <c>metadata.namespace</c> (CRs may live in any namespace, doc 04).</param>
/// <param name="Generation">The CR's <c>metadata.generation</c>, echoed into <c>status.observedGeneration</c>.</param>
/// <param name="Spec">The CR's raw <c>spec</c> element, mapped by <see cref="Mapping.CustomResourceSpecMapper"/>.</param>
public sealed record CustomResourceRecord(string Name, string Namespace, long Generation, JsonElement Spec)
{
    /// <summary>Gets the stable identity key for this CR across namespaces (<c>namespace/name</c>).</summary>
    /// <value>The <c>namespace/name</c> key.</value>
    public string Key => $"{Namespace}/{Name}";
}
