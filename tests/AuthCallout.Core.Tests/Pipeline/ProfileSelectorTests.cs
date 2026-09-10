// -----------------------------------------------------------------------
// <copyright file="ProfileSelectorTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Core.Claims;
using AuthCallout.Core.Pipeline;

namespace AuthCallout.Core.Tests.Pipeline;

/// <summary>
/// Table tests for <see cref="ProfileSelector"/> (doc 03 Stage 4, ADR 0001 §First-match-wins):
/// first-match ordering, no-match denial, and "multiple matches are not an error" (only the
/// first is ever consulted).
/// </summary>
public sealed class ProfileSelectorTests
{
    [Fact]
    public void SelectFirstMatch_FirstMatchingProfileWins_EvenWhenLaterProfileAlsoMatches()
    {
        // ADR 0001: "order is the tie-breaker; multiple matches are not an error."
        var first = PipelineTestBuilders.Profile(name: "first", match: MatchOf("role", "member"));
        var second = PipelineTestBuilders.Profile(name: "second", match: MatchOf("role", "member"));
        var claims = ClaimsAccessor.Parse("""{ "role": "member" }""");

        var selected = ProfileSelector.SelectFirstMatch([first, second], claims);

        Assert.Equal(first, selected);
    }

    [Fact]
    public void SelectFirstMatch_EarlierProfileDoesNotMatch_SelectsLaterMatchingProfile()
    {
        var admin = PipelineTestBuilders.Profile(name: "admin", match: MatchOf("role", "admin"));
        var member = PipelineTestBuilders.Profile(name: "member", match: MatchOf("role", "member"));
        var claims = ClaimsAccessor.Parse("""{ "role": "member" }""");

        var selected = ProfileSelector.SelectFirstMatch([admin, member], claims);

        Assert.Equal(member, selected);
    }

    [Fact]
    public void SelectFirstMatch_NoProfileMatches_ReturnsNull()
    {
        var admin = PipelineTestBuilders.Profile(name: "admin", match: MatchOf("role", "admin"));
        var claims = ClaimsAccessor.Parse("""{ "role": "guest" }""");

        var selected = ProfileSelector.SelectFirstMatch([admin], claims);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectFirstMatch_EmptyProfileList_ReturnsNull()
    {
        var claims = ClaimsAccessor.Parse("{}");

        var selected = ProfileSelector.SelectFirstMatch([], claims);

        Assert.Null(selected);
    }

    private static ClaimMatch MatchOf(string claimPath, string value) =>
        new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [claimPath] = [value] });
}
