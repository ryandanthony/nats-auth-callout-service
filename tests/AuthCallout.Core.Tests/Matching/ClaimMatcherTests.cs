// -----------------------------------------------------------------------
// <copyright file="ClaimMatcherTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Core.Claims;
using AuthCallout.Core.Matching;

namespace AuthCallout.Core.Tests.Matching;

/// <summary>
/// Table tests for <see cref="ClaimMatcher"/>: OR-within-key, AND-across-keys, nested paths,
/// array claims, and empty/absent handling (doc 02 §Claim match).
/// </summary>
public sealed class ClaimMatcherTests
{
    [Fact]
    public void IsMatch_EmptyMatch_AlwaysMatches()
    {
        var claims = Build("""{ "sub": "user-1" }""");

        Assert.True(ClaimMatcher.IsMatch(ClaimMatch.Empty, claims));
    }

    [Fact]
    public void IsMatch_EmptyMatch_MatchesEvenWithNoClaimsAtAll()
    {
        var claims = Build("{}");

        Assert.True(ClaimMatcher.IsMatch(ClaimMatch.Empty, claims));
    }

    [Fact]
    public void IsMatch_SingleKeyScalarClaim_ExactValue_Matches()
    {
        var match = MatchOf(("azp", ["billing-service"]));
        var claims = Build("""{ "azp": "billing-service" }""");

        Assert.True(ClaimMatcher.IsMatch(match, claims));
    }

    [Fact]
    public void IsMatch_SingleKeyScalarClaim_WrongValue_DoesNotMatch()
    {
        var match = MatchOf(("azp", ["billing-service"]));
        var claims = Build("""{ "azp": "other-service" }""");

        Assert.False(ClaimMatcher.IsMatch(match, claims));
    }

    [Fact]
    public void IsMatch_ArrayClaim_ContainsOneListedValue_MatchesOr()
    {
        // OR within a key: the array claim contains one of several accepted values.
        var match = MatchOf(("realm_access.roles", ["admin", "superuser"]));
        var claims = Build("""{ "realm_access": { "roles": ["user", "admin"] } }""");

        Assert.True(ClaimMatcher.IsMatch(match, claims));
    }

    [Fact]
    public void IsMatch_ArrayClaim_ContainsNoneOfListedValues_DoesNotMatch()
    {
        var match = MatchOf(("realm_access.roles", ["admin", "superuser"]));
        var claims = Build("""{ "realm_access": { "roles": ["user", "guest"] } }""");

        Assert.False(ClaimMatcher.IsMatch(match, claims));
    }

    [Fact]
    public void IsMatch_MultipleKeys_AllMatch_MatchesAnd()
    {
        // AND across keys: both predicates must be satisfied.
        var match = MatchOf(
            ("azp", ["billing-service"]),
            ("realm_access.roles", ["service"]));
        var claims = Build("""{ "azp": "billing-service", "realm_access": { "roles": ["service"] } }""");

        Assert.True(ClaimMatcher.IsMatch(match, claims));
    }

    [Fact]
    public void IsMatch_MultipleKeys_OneFails_DoesNotMatch()
    {
        var match = MatchOf(
            ("azp", ["billing-service"]),
            ("realm_access.roles", ["service"]));
        var claims = Build("""{ "azp": "billing-service", "realm_access": { "roles": ["other"] } }""");

        Assert.False(ClaimMatcher.IsMatch(match, claims));
    }

    [Fact]
    public void IsMatch_ClaimAbsent_DoesNotMatch()
    {
        var match = MatchOf(("azp", ["billing-service"]));
        var claims = Build("""{ "sub": "user-1" }""");

        Assert.False(ClaimMatcher.IsMatch(match, claims));
    }

    [Fact]
    public void IsMatch_AcceptedValuesEmptyForKey_NeverMatches()
    {
        var match = MatchOf(("azp", []));
        var claims = Build("""{ "azp": "billing-service" }""");

        Assert.False(ClaimMatcher.IsMatch(match, claims));
    }

    [Fact]
    public void IsMatch_ValueComparisonIsOrdinal_CaseSensitive()
    {
        var match = MatchOf(("azp", ["Billing-Service"]));
        var claims = Build("""{ "azp": "billing-service" }""");

        Assert.False(ClaimMatcher.IsMatch(match, claims));
    }

    [Fact]
    public void IsMatch_NullMatchArgument_Throws()
    {
        var claims = Build("{}");

        Assert.Throws<ArgumentNullException>(() => ClaimMatcher.IsMatch(null!, claims));
    }

    [Fact]
    public void IsMatch_NullClaimsArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ClaimMatcher.IsMatch(ClaimMatch.Empty, null!));
    }

    private static ClaimMatch MatchOf(params (string claimPath, string[] values)[] predicates)
    {
        var dict = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (claimPath, values) in predicates)
        {
            dict[claimPath] = values;
        }

        return new ClaimMatch(dict);
    }

    private static ClaimsAccessor Build(string json) => ClaimsAccessor.Parse(json);
}
