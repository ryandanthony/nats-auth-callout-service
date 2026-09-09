// -----------------------------------------------------------------------
// <copyright file="CustomResourceStatusDecision.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Config.K8s.Client;

namespace AuthCallout.Config.K8s.Aggregation;

/// <summary>
/// What <see cref="CrdRoutingConfigProvider"/> should write back to one Custom Resource's
/// <c>status</c> subresource after an aggregation decision (doc 09 §4 §status subresource,
/// ADR 0006): <c>accepted</c>, <c>reason</c> (on rejection), <c>observedGeneration</c>. A CR
/// rejected only because a sibling CR broke the batch gets a distinct reason naming the
/// offender(s), per the packet's ADR→test map ("batch-rejected-by-sibling gets a distinct
/// reason").
/// </summary>
/// <param name="Resource">Which CR this status applies to.</param>
/// <param name="Accepted">Whether this CR's application was accepted into the active batch.</param>
/// <param name="Reason">The rejection reason. Empty when <paramref name="Accepted"/> is true.</param>
public sealed record CustomResourceStatusDecision(CustomResourceRecord Resource, bool Accepted, string Reason)
{
    /// <summary>Creates an accepted-status decision.</summary>
    /// <param name="resource">The accepted CR.</param>
    /// <returns>The constructed decision.</returns>
    public static CustomResourceStatusDecision ForAccepted(CustomResourceRecord resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return new CustomResourceStatusDecision(resource, Accepted: true, Reason: string.Empty);
    }

    /// <summary>Creates a rejected-status decision for a CR that itself failed mapping/validation.</summary>
    /// <param name="resource">The rejected CR.</param>
    /// <param name="reason">The CR's own rejection reason.</param>
    /// <returns>The constructed decision.</returns>
    public static CustomResourceStatusDecision Rejected(CustomResourceRecord resource, string reason)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new CustomResourceStatusDecision(resource, Accepted: false, reason);
    }

    /// <summary>Creates a rejected-status decision for a CR that was otherwise valid but the batch was rejected by a sibling CR (ADR 0006 atomicity).</summary>
    /// <param name="resource">The otherwise-valid CR.</param>
    /// <param name="offenderDescription">A description of the offending sibling(s).</param>
    /// <returns>The constructed decision.</returns>
    public static CustomResourceStatusDecision SiblingRejected(CustomResourceRecord resource, string offenderDescription)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(offenderDescription);
        return new CustomResourceStatusDecision(
            resource,
            Accepted: false,
            $"Batch rejected because of sibling Custom Resource(s): {offenderDescription}");
    }
}
