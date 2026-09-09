// -----------------------------------------------------------------------
// <copyright file="ICrdKubernetesClient.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Config.K8s.Client;

/// <summary>
/// A thin seam over the KubernetesClient operations
/// <see cref="AuthCallout.Config.K8s.CrdRoutingConfigProvider"/> needs: list-and-watch
/// <c>authcallout.ants.zone/v1 AuthApplication</c> Custom Resources owned by this instance's
/// label selector, and patch each CR's <c>status</c> subresource (doc 09 §4, ADR 0006). Exists
/// so the provider's relist/backoff/mapping/status-write-back logic is unit-testable against a
/// faked event stream without a cluster (P11 packet §Testing strategy); the real implementation
/// (<see cref="CrdKubernetesClient"/>) is the only type in this project that references
/// <c>k8s.Kubernetes</c> directly.
/// </summary>
public interface ICrdKubernetesClient
{
    /// <summary>
    /// Lists every <c>AuthApplication</c> Custom Resource matching the owner label selector,
    /// cluster-wide, across all namespaces (doc 04: "CRs may live in any namespace").
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full owned set and the resource version to watch from.</returns>
    Task<CrdListResult> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Watches for changes to owned Custom Resources starting from <paramref name="resourceVersion"/>.
    /// </summary>
    /// <param name="resourceVersion">The resource version to watch from, from a prior <see cref="ListAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An async sequence of change events. Throws <see cref="CrdWatchGoneException"/> when the
    /// server reports <c>410 Gone</c> (caller must relist); the sequence otherwise ends only on
    /// cancellation or an unrecoverable connection failure.
    /// </returns>
    IAsyncEnumerable<CrdChangeEvent> WatchAsync(string resourceVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Patches a Custom Resource's <c>status</c> subresource (doc 09 §4, ADR 0006). Failures are
    /// the caller's concern to log; this method never throws for an ordinary API rejection
    /// (e.g. the CR was deleted between the batch decision and the patch) — it returns without
    /// effect. It may still throw for transport-level failures the caller cannot recover from
    /// mid-batch.
    /// </summary>
    /// <param name="resource">Identifies which CR to patch (by name/namespace).</param>
    /// <param name="observedGeneration">The generation the status reflects.</param>
    /// <param name="accepted">Whether this CR's application was accepted into the active batch.</param>
    /// <param name="reason">The rejection reason, when <paramref name="accepted"/> is false; otherwise empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the patch attempt (successful or not) finishes.</returns>
    Task PatchStatusAsync(
        CustomResourceRecord resource, long observedGeneration, bool accepted, string reason, CancellationToken cancellationToken);
}
