// -----------------------------------------------------------------------
// <copyright file="AppSelectorTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Core.Claims;
using AuthCallout.Core.Pipeline;

namespace AuthCallout.Core.Tests.Pipeline;

/// <summary>
/// Table tests for <see cref="AppSelector"/> (doc 03 Stage 3, ADR 0003 §Application `Match`):
/// exactly-one-match success, zero-match and ambiguous-match denials, and the null-match
/// "issuer alone identifies it" shortcut.
/// </summary>
public sealed class AppSelectorTests
{
    [Fact]
    public void Select_SingleCandidateWithNullMatch_SelectsIt()
    {
        // ADR 0003: "A single candidate with a unique issuer skips this step."
        var app = PipelineTestBuilders.Application(match: null);
        var claims = ClaimsAccessor.Parse("{}");

        var result = AppSelector.Select([app], claims);

        Assert.Equal(app, result.Application);
        Assert.Null(result.DenyReason);
    }

    [Fact]
    public void Select_OneCandidateMatches_SelectsIt()
    {
        var matching = PipelineTestBuilders.Application(name: "notes", match: MatchOf("aud", "notes-api"));
        var other = PipelineTestBuilders.Application(name: "tasks", match: MatchOf("aud", "tasks-api"));
        var claims = ClaimsAccessor.Parse("""{ "aud": "notes-api" }""");

        var result = AppSelector.Select([matching, other], claims);

        Assert.Equal(matching, result.Application);
        Assert.Null(result.DenyReason);
    }

    [Fact]
    public void Select_ZeroCandidatesMatch_DeniesNoAppMatch()
    {
        var app1 = PipelineTestBuilders.Application(name: "notes", match: MatchOf("aud", "notes-api"));
        var app2 = PipelineTestBuilders.Application(name: "tasks", match: MatchOf("aud", "tasks-api"));
        var claims = ClaimsAccessor.Parse("""{ "aud": "other-api" }""");

        var result = AppSelector.Select([app1, app2], claims);

        Assert.Null(result.Application);
        Assert.Equal(DenyReason.NoAppMatch, result.DenyReason);
    }

    [Fact]
    public void Select_MoreThanOneCandidateMatches_DeniesAmbiguousApp_NoTieBreaker()
    {
        // ADR 0003: "no union, no tie-breaker" when more than one application matches.
        var app1 = PipelineTestBuilders.Application(name: "notes", match: ClaimMatch.Empty);
        var app2 = PipelineTestBuilders.Application(name: "tasks", match: ClaimMatch.Empty);
        var claims = ClaimsAccessor.Parse("{}");

        var result = AppSelector.Select([app1, app2], claims);

        Assert.Null(result.Application);
        Assert.Equal(DenyReason.AmbiguousApp, result.DenyReason);
    }

    private static ClaimMatch MatchOf(string claimPath, string value) =>
        new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [claimPath] = [value] });
}
