// -----------------------------------------------------------------------
// <copyright file="AnonymousComposerTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Core.Pipeline;

namespace AuthCallout.Core.Tests.Pipeline;

/// <summary>
/// Golden + table tests for <see cref="AnonymousComposer"/> (doc 03 Stage 0, ADR 0004):
/// union-across-apps composition, de-duplication, opt-out (no <c>anonymousProfile</c>), the
/// zero-contributions denial (D15), no-KV-reads-ever, and expiry sourced from the global
/// bootstrap config rather than any token.
/// </summary>
public sealed class AnonymousComposerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000);

    [Fact]
    public void Compose_UnionAcrossApplications_CombinesContributions()
    {
        // ADR 0004: the anonymous grant is the union of every configured application's
        // optional AnonymousProfile contribution.
        var notes = PipelineTestBuilders.Application(name: "notes", anonymousProfile: new Grant(["notes.public"], ["notes.announcements"]));
        var tasks = PipelineTestBuilders.Application(name: "tasks", anonymousProfile: new Grant(["tasks.public"], ["tasks.announcements"]));
        var routing = PipelineTestBuilders.Routing(notes, tasks);

        var result = AnonymousComposer.Compose(routing, PipelineTestBuilders.Anonymous(), Now);

        Assert.Null(result.DenyReason);
        Assert.Equal(["notes.public", "tasks.public"], result.Decision!.Publish);
        Assert.Equal(["notes.announcements", "tasks.announcements"], result.Decision!.Subscribe);
    }

    [Fact]
    public void Compose_DuplicateSubjectsAcrossApplications_AreDeduplicated()
    {
        var a = PipelineTestBuilders.Application(name: "a", anonymousProfile: new Grant(["_INBOX.>"], []));
        var b = PipelineTestBuilders.Application(name: "b", anonymousProfile: new Grant(["_INBOX.>"], []));
        var routing = PipelineTestBuilders.Routing(a, b);

        var result = AnonymousComposer.Compose(routing, PipelineTestBuilders.Anonymous(), Now);

        Assert.Equal(["_INBOX.>"], result.Decision!.Publish);
    }

    [Fact]
    public void Compose_ApplicationWithNoAnonymousProfile_ContributesNothing_OptOut()
    {
        var contributing = PipelineTestBuilders.Application(name: "notes", anonymousProfile: new Grant(["notes.public"], []));
        var optedOut = PipelineTestBuilders.Application(name: "tasks", anonymousProfile: null);
        var routing = PipelineTestBuilders.Routing(contributing, optedOut);

        var result = AnonymousComposer.Compose(routing, PipelineTestBuilders.Anonymous(), Now);

        Assert.Equal(["notes.public"], result.Decision!.Publish);
    }

    [Fact]
    public void Compose_ZeroContributionsAcrossAllApplications_DeniesAnonymousNoContributions()
    {
        // D15: zero anonymousProfile contributions across every application = deny.
        var app = PipelineTestBuilders.Application(anonymousProfile: null);
        var routing = PipelineTestBuilders.Routing(app);

        var result = AnonymousComposer.Compose(routing, PipelineTestBuilders.Anonymous(), Now);

        Assert.Null(result.Decision);
        Assert.Equal(DenyReason.AnonymousNoContributions, result.DenyReason);
    }

    [Fact]
    public void Compose_NoApplicationsConfigured_DeniesAnonymousNoContributions()
    {
        var result = AnonymousComposer.Compose(RoutingConfig.Empty, PipelineTestBuilders.Anonymous(), Now);

        Assert.Equal(DenyReason.AnonymousNoContributions, result.DenyReason);
    }

    [Fact]
    public void Compose_GrantsNoKvReadSubjects_Ever()
    {
        // doc 03 §Grant construction: "no KV reads ever" for anonymous.
        var app = PipelineTestBuilders.Application(anonymousProfile: new Grant(["a.>"], ["b.>"]));
        var routing = PipelineTestBuilders.Routing(app);

        var result = AnonymousComposer.Compose(routing, PipelineTestBuilders.Anonymous(), Now);

        Assert.DoesNotContain(result.Decision!.Publish, s => s.StartsWith("$KV.", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Decision!.Subscribe, s => s.StartsWith("$KV.", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_TargetsGlobalAnonymousAccount_WithGlobalExpiry_NotAnyApplicationAccount()
    {
        var app = PipelineTestBuilders.Application(name: "notes", anonymousProfile: new Grant(["notes.public"], []));
        var routing = PipelineTestBuilders.Routing(app);
        var anonymous = PipelineTestBuilders.Anonymous(targetAccount: "ANON", expiry: "10m");

        var result = AnonymousComposer.Compose(routing, anonymous, Now);

        Assert.Equal("ANON", result.Decision!.TargetAccount);
        Assert.Equal(Now + TimeSpan.FromMinutes(10), result.Decision!.Expiry);
    }

    [Fact]
    public void Compose_AnonymousDecisionHasNoApplicationPrincipalIssuerOrProfile()
    {
        var app = PipelineTestBuilders.Application(anonymousProfile: new Grant(["a.>"], []));
        var routing = PipelineTestBuilders.Routing(app);

        var result = AnonymousComposer.Compose(routing, PipelineTestBuilders.Anonymous(), Now);

        var decision = result.Decision!;
        Assert.Null(decision.Application);
        Assert.Null(decision.Principal);
        Assert.Null(decision.Issuer);
        Assert.Null(decision.ProfileName);
        Assert.False(decision.IdentityPropagationEnabled);
        Assert.Null(decision.SelfLookupKey);
    }
}
