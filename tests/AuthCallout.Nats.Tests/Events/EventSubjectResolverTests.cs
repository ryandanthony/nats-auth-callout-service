// -----------------------------------------------------------------------
// <copyright file="EventSubjectResolverTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Nats.Events;

namespace AuthCallout.Nats.Tests.Events;

/// <summary>
/// Unit tests for <see cref="EventSubjectResolver"/> (P09 ADR→test map: "Subject routing
/// incl. unconfigured-subject skip (doc 05)", table-driven).
/// </summary>
public sealed class EventSubjectResolverTests
{
    [Fact]
    public void Resolve_AppGrantedDecision_UsesSelectedApplicationsSuccessSubject()
    {
        var app = EventTestBuilders.ApplicationWithEvents("notes", new EventSubjects(Success: "notes.events.success", Failure: "notes.events.failure"));
        var routing = EventTestBuilders.RoutingFor(app);
        var decision = GrantedFor("notes");

        var subject = EventSubjectResolver.Resolve(decision, routing, anonymousEvents: null);

        Assert.Equal("notes.events.success", subject);
    }

    [Fact]
    public void Resolve_AppDeniedDecision_UsesSelectedApplicationsFailureSubject()
    {
        var app = EventTestBuilders.ApplicationWithEvents("notes", new EventSubjects(Success: "notes.events.success", Failure: "notes.events.failure"));
        var routing = EventTestBuilders.RoutingFor(app);
        var decision = new DeniedDecision(Application: "notes", Principal: null, Issuer: null, Reason: DenyReason.NoProfileMatch);

        var subject = EventSubjectResolver.Resolve(decision, routing, anonymousEvents: null);

        Assert.Equal("notes.events.failure", subject);
    }

    [Fact]
    public void Resolve_AnonymousGranted_UsesGlobalAnonymousSuccessSubject()
    {
        var routing = EventTestBuilders.RoutingFor();
        var decision = GrantedFor(application: null);
        var anonymousEvents = new EventSubjects(Success: "anon.events.success", Failure: "anon.events.failure");

        var subject = EventSubjectResolver.Resolve(decision, routing, anonymousEvents);

        Assert.Equal("anon.events.success", subject);
    }

    [Fact]
    public void Resolve_DeniedBeforeAppSelection_FallsBackToGlobalAnonymousFailureSubject()
    {
        // Doc 05's flowchart has exactly three outcome branches: app success, app failure,
        // anonymous. A denial with no Application (unknown issuer, invalid token, no/ambiguous
        // app match) has no per-app subject to use, so it falls back to the global anonymous
        // failure subject if configured.
        var routing = EventTestBuilders.RoutingFor();
        var decision = new DeniedDecision(Application: null, Principal: null, Issuer: null, Reason: DenyReason.UnknownIssuer);
        var anonymousEvents = new EventSubjects(Success: "anon.events.success", Failure: "anon.events.failure");

        var subject = EventSubjectResolver.Resolve(decision, routing, anonymousEvents);

        Assert.Equal("anon.events.failure", subject);
    }

    [Fact]
    public void Resolve_AppHasNoEventsConfigured_ReturnsNull_SkipSilently()
    {
        var app = EventTestBuilders.ApplicationWithEvents("notes", events: null);
        var routing = EventTestBuilders.RoutingFor(app);
        var decision = GrantedFor("notes");

        var subject = EventSubjectResolver.Resolve(decision, routing, anonymousEvents: null);

        Assert.Null(subject);
    }

    [Fact]
    public void Resolve_AppEventsConfigured_ButSuccessSideNull_ReturnsNull_OnSuccess()
    {
        var app = EventTestBuilders.ApplicationWithEvents("notes", new EventSubjects(Success: null, Failure: "notes.events.failure"));
        var routing = EventTestBuilders.RoutingFor(app);
        var decision = GrantedFor("notes");

        var subject = EventSubjectResolver.Resolve(decision, routing, anonymousEvents: null);

        Assert.Null(subject);
    }

    [Fact]
    public void Resolve_AnonymousEventsNotConfigured_ReturnsNull_NoDefaultSubject()
    {
        // ADR 0008: "There is no default subject, so anonymous events are off unless a
        // subject is set."
        var routing = EventTestBuilders.RoutingFor();
        var decision = GrantedFor(application: null);

        var subject = EventSubjectResolver.Resolve(decision, routing, anonymousEvents: null);

        Assert.Null(subject);
    }

    [Fact]
    public void Resolve_ApplicationSelectedButNotInRoutingAggregate_ReturnsNull()
    {
        // Defensive: a decision naming an application no longer in the aggregate (e.g. a
        // race with a reload) resolves to no subject rather than throwing.
        var routing = EventTestBuilders.RoutingFor();
        var decision = GrantedFor("ghost-app");

        var subject = EventSubjectResolver.Resolve(decision, routing, anonymousEvents: null);

        Assert.Null(subject);
    }

    [Fact]
    public void Resolve_NullDecision_ThrowsArgumentNullException()
    {
        var routing = EventTestBuilders.RoutingFor();

        Assert.Throws<ArgumentNullException>(() => EventSubjectResolver.Resolve(null!, routing, null));
    }

    [Fact]
    public void Resolve_NullRouting_ThrowsArgumentNullException()
    {
        var decision = GrantedFor("notes");

        Assert.Throws<ArgumentNullException>(() => EventSubjectResolver.Resolve(decision, null!, null));
    }

    private static GrantedDecision GrantedFor(string? application) => new(
        Application: application,
        Principal: application is null ? null : "user@example.com",
        Issuer: application is null ? null : "https://id.example.com",
        TargetAccount: "ACCT",
        Name: "user",
        Expiry: DateTimeOffset.UtcNow.AddMinutes(5),
        Publish: [],
        Subscribe: [],
        ProfileName: application is null ? null : "editor",
        IdentityPropagationEnabled: false,
        SelfLookupKey: null);
}
