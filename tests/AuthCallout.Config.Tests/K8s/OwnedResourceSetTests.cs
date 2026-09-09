// -----------------------------------------------------------------------
// <copyright file="OwnedResourceSetTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Config.K8s.Aggregation;
using AuthCallout.Config.K8s.Client;

namespace AuthCallout.Config.Tests.K8s;

/// <summary>
/// Unit tests for <see cref="OwnedResourceSet"/>: pure aggregation/validation logic, no client
/// or cluster. Covers ADR 0006 atomicity (one bad CR rejects the whole batch, each CR's status
/// decision distinguishes "my own fault" from "a sibling's fault") and basic add/modify/delete
/// tracking.
/// </summary>
public sealed class OwnedResourceSetTests
{
    [Fact]
    public void EmptySet_AggregatesToEmptyValidBatch()
    {
        var set = new OwnedResourceSet();

        var decision = set.Aggregate();

        Assert.True(decision.IsSuccess);
        Assert.Empty(decision.Routing!.Applications);
        Assert.Empty(decision.Statuses);
    }

    [Fact]
    public void SingleValidResource_Aggregates_AndStatusIsAccepted()
    {
        var set = new OwnedResourceSet();
        set.Reset([MakeResource("notes", "notes", "NOTES")]);

        var decision = set.Aggregate();

        Assert.True(decision.IsSuccess);
        var application = Assert.Single(decision.Routing!.Applications);
        Assert.Equal("notes", application.Name);
        var status = Assert.Single(decision.Statuses);
        Assert.True(status.Accepted);
        Assert.Empty(status.Reason);
    }

    [Fact]
    public void Apply_Added_TracksNewResource()
    {
        var set = new OwnedResourceSet();

        set.Apply(new CrdChangeEvent(CrdChangeKind.Added, MakeResource("notes", "notes", "NOTES")));

        Assert.Equal(1, set.Count);
        var decision = set.Aggregate();
        Assert.True(decision.IsSuccess);
        Assert.Single(decision.Routing!.Applications);
    }

    [Fact]
    public void Apply_Modified_ReplacesExistingResource()
    {
        var set = new OwnedResourceSet();
        set.Apply(new CrdChangeEvent(CrdChangeKind.Added, MakeResource("notes", "notes", "NOTES")));

        set.Apply(new CrdChangeEvent(CrdChangeKind.Modified, MakeResource("notes", "notes", "NOTES2")));

        Assert.Equal(1, set.Count);
        var decision = set.Aggregate();
        Assert.Equal("NOTES2", decision.Routing!.Applications[0].TargetAccount);
    }

    [Fact]
    public void Apply_Deleted_RemovesResource()
    {
        var set = new OwnedResourceSet();
        set.Apply(new CrdChangeEvent(CrdChangeKind.Added, MakeResource("notes", "notes", "NOTES")));

        set.Apply(new CrdChangeEvent(CrdChangeKind.Deleted, MakeResource("notes", "notes", "NOTES")));

        Assert.Equal(0, set.Count);
        var decision = set.Aggregate();
        Assert.Empty(decision.Routing!.Applications);
    }

    [Fact]
    public void Reset_ReplacesEntireTrackedSet()
    {
        var set = new OwnedResourceSet();
        set.Apply(new CrdChangeEvent(CrdChangeKind.Added, MakeResource("stale", "stale", "STALE")));

        set.Reset([MakeResource("notes", "notes", "NOTES")]);

        Assert.Equal(1, set.Count);
        var decision = set.Aggregate();
        var application = Assert.Single(decision.Routing!.Applications);
        Assert.Equal("notes", application.Name);
    }

    [Fact]
    public void OneMalformedResource_RejectsWholeBatch_ErrorNamesIt()
    {
        var set = new OwnedResourceSet();
        set.Reset(
        [
            MakeResource("notes", "notes", "NOTES"),
            MakeMalformedResource("broken", "broken"),
        ]);

        var decision = set.Aggregate();

        Assert.False(decision.IsSuccess);
        Assert.Contains("broken", decision.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void OneMalformedResource_SelfStatusIsRejectedWithOwnReason()
    {
        var set = new OwnedResourceSet();
        set.Reset(
        [
            MakeResource("notes", "notes", "NOTES"),
            MakeMalformedResource("broken", "broken"),
        ]);

        var decision = set.Aggregate();

        var brokenStatus = decision.Statuses.Single(s => string.Equals(s.Resource.Name, "broken", StringComparison.Ordinal));
        Assert.False(brokenStatus.Accepted);
        Assert.Contains("targetAccount", brokenStatus.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void OneMalformedResource_SiblingStatusIsRejectedWithDistinctReason()
    {
        var set = new OwnedResourceSet();
        set.Reset(
        [
            MakeResource("notes", "notes", "NOTES"),
            MakeMalformedResource("broken", "broken"),
        ]);

        var decision = set.Aggregate();

        var notesStatus = decision.Statuses.Single(s => string.Equals(s.Resource.Name, "notes", StringComparison.Ordinal));
        Assert.False(notesStatus.Accepted);
        Assert.Contains("sibling", notesStatus.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("broken", notesStatus.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateApplicationNames_RejectsWholeBatch()
    {
        var set = new OwnedResourceSet();
        set.Reset(
        [
            MakeResource("ns-a", "notes", "NOTES"),
            MakeResource("ns-b", "notes", "NOTES2"),
        ]);

        var decision = set.Aggregate();

        Assert.False(decision.IsSuccess);
        Assert.Contains("unique", decision.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateApplicationNames_BothGetSelfRejectedStatus()
    {
        var set = new OwnedResourceSet();
        set.Reset(
        [
            MakeResource("ns-a", "notes", "NOTES"),
            MakeResource("ns-b", "notes", "NOTES2"),
        ]);

        var decision = set.Aggregate();

        Assert.All(decision.Statuses, static s => Assert.False(s.Accepted));
        Assert.All(decision.Statuses, static s => Assert.Contains("notes", s.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidBatchAfterRejectedBatch_AggregatesCleanly()
    {
        var set = new OwnedResourceSet();
        set.Reset(
        [
            MakeResource("notes", "notes", "NOTES"),
            MakeMalformedResource("broken", "broken"),
        ]);
        Assert.False(set.Aggregate().IsSuccess);

        set.Reset([MakeResource("notes", "notes", "NOTES")]);
        var decision = set.Aggregate();

        Assert.True(decision.IsSuccess);
        Assert.Single(decision.Routing!.Applications);
    }

    private static CustomResourceRecord MakeResource(string ns, string name, string targetAccount)
    {
        var specJson = $$"""
            {
              "targetAccount": "{{targetAccount}}",
              "issuer": {
                "authority": "https://id.example.com/realms/{{name}}",
                "jwksCache": "5m",
                "validateAudience": false
              },
              "profiles": []
            }
            """;
        using var doc = JsonDocument.Parse(specJson);
        return new CustomResourceRecord(name, ns, Generation: 1, doc.RootElement.Clone());
    }

    private static CustomResourceRecord MakeMalformedResource(string ns, string name)
    {
        // Missing required 'targetAccount' and 'issuer'.
        using var doc = JsonDocument.Parse("""{ "profiles": [] }""");
        return new CustomResourceRecord(name, ns, Generation: 1, doc.RootElement.Clone());
    }
}
