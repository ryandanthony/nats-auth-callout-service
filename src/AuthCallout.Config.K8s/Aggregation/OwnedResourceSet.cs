// -----------------------------------------------------------------------
// <copyright file="OwnedResourceSet.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Config.K8s.Client;
using AuthCallout.Config.K8s.Mapping;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Validation;

namespace AuthCallout.Config.K8s.Aggregation;

/// <summary>
/// Tracks the current set of owned <c>AuthApplication</c> Custom Resources (keyed by
/// <see cref="CustomResourceRecord.Key"/>, i.e. <c>namespace/name</c>) and turns it into one
/// validated <see cref="RoutingConfig"/> aggregate on demand (P11 packet: "Any add/modify/delete
/// → re-aggregate the full owned set → validate → emit batch", ADR 0006 atomicity). Pure and
/// client-free, so it is unit-testable without KubernetesClient or a cluster.
/// </summary>
public sealed class OwnedResourceSet
{
    private readonly Dictionary<string, CustomResourceRecord> _resources = new(StringComparer.Ordinal);

    /// <summary>Gets the number of currently-tracked Custom Resources.</summary>
    /// <value>The tracked resource count.</value>
    public int Count => _resources.Count;

    /// <summary>Applies one watch event (add/modify/delete) to the tracked set.</summary>
    /// <param name="changeEvent">The event to apply.</param>
    public void Apply(CrdChangeEvent changeEvent)
    {
        ArgumentNullException.ThrowIfNull(changeEvent);

        if (changeEvent.Kind == CrdChangeKind.Deleted)
        {
            _resources.Remove(changeEvent.Resource.Key);
        }
        else
        {
            _resources[changeEvent.Resource.Key] = changeEvent.Resource;
        }
    }

    /// <summary>Replaces the entire tracked set (used after a fresh list/relist).</summary>
    /// <param name="resources">The full owned set as of the list.</param>
    public void Reset(IEnumerable<CustomResourceRecord> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _resources.Clear();
        foreach (var resource in resources)
        {
            _resources[resource.Key] = resource;
        }
    }

    /// <summary>
    /// Maps and validates the current tracked set into an aggregation decision (ADR 0006: the
    /// whole batch is atomic — either every CR's mapping succeeds and the aggregate validates,
    /// producing an emittable batch, or the decision carries per-CR rejection reasons and no
    /// batch is emitted).
    /// </summary>
    /// <returns>The aggregation decision.</returns>
    public AggregationDecision Aggregate()
    {
        var mapped = new List<MappedResource>();
        foreach (var resource in _resources.Values)
        {
            mapped.Add(new MappedResource(resource, CustomResourceSpecMapper.TryMap(resource.Name, resource.Spec)));
        }

        var mappingFailures = mapped.Where(static m => !m.Result.IsSuccess).ToList();
        if (mappingFailures.Count > 0)
        {
            return BuildMappingFailureDecision(mapped, mappingFailures);
        }

        var applications = mapped.ConvertAll(static m => m.Result.Application!);
        var routing = new RoutingConfig(applications);
        var validation = ConfigValidator.ValidateRouting(routing);

        if (!validation.IsValid)
        {
            return BuildValidationFailureDecision(mapped, validation);
        }

        var statuses = mapped.ConvertAll(static m => CustomResourceStatusDecision.ForAccepted(m.Resource));
        return AggregationDecision.Success(routing, statuses);
    }

    private static AggregationDecision BuildMappingFailureDecision(
        List<MappedResource> mapped, List<MappedResource> mappingFailures)
    {
        var offenderNames = string.Join(", ", mappingFailures.Select(static m => $"'{m.Resource.Name}'"));
        var statuses = new List<CustomResourceStatusDecision>();
        foreach (var entry in mapped)
        {
            statuses.Add(entry.Result.IsSuccess
                ? CustomResourceStatusDecision.SiblingRejected(entry.Resource, offenderNames)
                : CustomResourceStatusDecision.Rejected(entry.Resource, entry.Result.Error!));
        }

        return AggregationDecision.Failure(
            $"Rejected batch: {mappingFailures.Count} Custom Resource(s) invalid (keeping last-good, ADR 0006): "
            + string.Join(" | ", mappingFailures.Select(static m => $"{m.Resource.Key}: {m.Result.Error}")),
            statuses);
    }

    private static AggregationDecision BuildValidationFailureDecision(List<MappedResource> mapped, ValidationResult validation)
    {
        var statuses = new List<CustomResourceStatusDecision>();
        foreach (var entry in mapped)
        {
            var applicationName = entry.Result.Application!.Name;
            var ownErrors = validation.Errors.Where(e => ReferencesApplication(e.Message, applicationName)).ToList();
            statuses.Add(ownErrors.Count > 0
                ? CustomResourceStatusDecision.Rejected(entry.Resource, string.Join(" ", ownErrors.Select(static e => e.Message)))
                : CustomResourceStatusDecision.SiblingRejected(entry.Resource, DescribeOtherOffenders(validation, applicationName)));
        }

        return AggregationDecision.Failure(
            "Rejected batch: aggregate validation failed (keeping last-good, ADR 0006): "
            + string.Join(" | ", validation.Errors.Select(static e => e.Message)),
            statuses);
    }

    /// <summary>
    /// Heuristically determines whether a validation error's deterministic message names a
    /// given application (every <c>ConfigValidator</c> rule message quotes the
    /// offending application name(s) in single quotes, e.g. <c>Application 'notes' ...</c>).
    /// </summary>
    private static bool ReferencesApplication(string errorMessage, string applicationName) =>
        errorMessage.Contains($"'{applicationName}'", StringComparison.Ordinal);

    private static string DescribeOtherOffenders(ValidationResult validation, string excludingApplicationName)
    {
        var messages = validation.Errors
            .Where(e => !ReferencesApplication(e.Message, excludingApplicationName))
            .Select(static e => e.Message)
            .ToList();
        return messages.Count > 0
            ? string.Join(" ", messages)
            : string.Join(" ", validation.Errors.Select(static e => e.Message));
    }

    /// <summary>One tracked CR paired with its mapping attempt, used only while building an <see cref="AggregationDecision"/>.</summary>
    private sealed record MappedResource(CustomResourceRecord Resource, CustomResourceMapResult Result);
}
