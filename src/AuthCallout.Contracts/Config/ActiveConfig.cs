// -----------------------------------------------------------------------
// <copyright file="ActiveConfig.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Config;

/// <summary>
/// An immutable snapshot pairing the bootstrap config with the currently active routing
/// aggregate and a monotonic version stamp (doc 04, doc 08 §Config reload → in-memory
/// swap). <see cref="Seams.IActiveConfigSource"/> publishes these by reference; a resolution in
/// flight keeps its snapshot for the whole request even if a newer one is swapped in
/// concurrently (ADR 0006 §Reload: "new connections use the new config").
/// </summary>
/// <param name="Bootstrap">The bootstrap layer (not hot-reloaded; the same instance across every snapshot for a process's lifetime).</param>
/// <param name="Routing">The currently active, validated routing aggregate.</param>
/// <param name="Version">A monotonically increasing stamp, incremented on every accepted reload; used for diagnostics and to detect a stale read without reference-comparing large graphs.</param>
public sealed record ActiveConfig(BootstrapConfig Bootstrap, RoutingConfig Routing, long Version);
