// -----------------------------------------------------------------------
// <copyright file="DenyReasonMapTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Decisions;

namespace AuthCallout.Contracts.Tests.Decisions;

/// <summary>
/// Exhaustiveness and wire-format tests for <see cref="DenyReasonMap"/> (QUESTIONS C12).
/// The exhaustiveness test enumerates <see cref="DenyReason"/> via reflection so a new
/// enum member added without a corresponding map entry fails CI immediately, rather than
/// throwing at runtime the first time that reason is hit.
/// </summary>
public sealed class DenyReasonMapTests
{
    [Fact]
    public void Map_HasAnEntryForEveryDenyReason()
    {
        var allReasons = Enum.GetValues<DenyReason>();

        var missing = allReasons.Where(r => !DenyReasonMap.All.ContainsKey(r)).ToArray();

        Assert.True(missing.Length == 0, $"DenyReasonMap is missing entries for: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Map_HasNoExtraEntriesBeyondDenyReason()
    {
        var allReasons = new HashSet<DenyReason>(Enum.GetValues<DenyReason>());

        var extra = DenyReasonMap.All.Keys.Where(k => !allReasons.Contains(k)).ToArray();

        Assert.True(extra.Length == 0, $"DenyReasonMap has entries for undefined DenyReason values: {string.Join(", ", extra)}");
    }

    [Theory]
    [InlineData(DenyReason.NoToken, EventReason.InvalidToken)]
    [InlineData(DenyReason.UnknownIssuer, EventReason.NoIssuerMatch)]
    [InlineData(DenyReason.SignatureInvalid, EventReason.InvalidToken)]
    [InlineData(DenyReason.Expired, EventReason.InvalidToken)]
    [InlineData(DenyReason.AudienceMismatch, EventReason.InvalidToken)]
    [InlineData(DenyReason.AlgorithmRejected, EventReason.InvalidToken)]
    [InlineData(DenyReason.NoAppMatch, EventReason.NoAppMatch)]
    [InlineData(DenyReason.AmbiguousApp, EventReason.AmbiguousApp)]
    [InlineData(DenyReason.NoProfileMatch, EventReason.NoProfileMatch)]
    [InlineData(DenyReason.RequiredVariableMissing, EventReason.GrantConstructionFailed)]
    [InlineData(DenyReason.UnsafeTemplateValue, EventReason.GrantConstructionFailed)]
    [InlineData(DenyReason.NameClaimError, EventReason.GrantConstructionFailed)]
    [InlineData(DenyReason.AnonymousNoContributions, EventReason.InvalidToken)]
    [InlineData(DenyReason.IdentityStoreWriteFailed, EventReason.GrantConstructionFailed)]
    public void ToEventReason_MapsExactlyAsSpecified(DenyReason reason, EventReason expected)
    {
        var actual = reason.ToEventReason();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EveryDenyReason_IsCoveredByATheoryCase()
    {
        // Guards the table above itself: if a new DenyReason is added, this fails until
        // a corresponding [InlineData] row (with its intended mapping) is added.
        var coveredByTheory = new HashSet<DenyReason>(
        [
            DenyReason.NoToken,
            DenyReason.UnknownIssuer,
            DenyReason.SignatureInvalid,
            DenyReason.Expired,
            DenyReason.AudienceMismatch,
            DenyReason.AlgorithmRejected,
            DenyReason.NoAppMatch,
            DenyReason.AmbiguousApp,
            DenyReason.NoProfileMatch,
            DenyReason.RequiredVariableMissing,
            DenyReason.UnsafeTemplateValue,
            DenyReason.NameClaimError,
            DenyReason.AnonymousNoContributions,
            DenyReason.IdentityStoreWriteFailed,
        ]);

        var allReasons = Enum.GetValues<DenyReason>();

        Assert.All(allReasons, r => Assert.Contains(r, coveredByTheory));
    }

    [Theory]
    [InlineData(EventReason.Anonymous, "anonymous")]
    [InlineData(EventReason.InvalidToken, "invalid_token")]
    [InlineData(EventReason.NoIssuerMatch, "no_issuer_match")]
    [InlineData(EventReason.NoAppMatch, "no_app_match")]
    [InlineData(EventReason.AmbiguousApp, "ambiguous_app")]
    [InlineData(EventReason.NoProfileMatch, "no_profile_match")]
    [InlineData(EventReason.GrantConstructionFailed, "grant_construction_failed")]
    [InlineData(EventReason.Granted, "granted")]
    public void ToWireString_MatchesDoc09ExactCodes(EventReason reason, string expectedWireValue)
    {
        Assert.Equal(expectedWireValue, reason.ToWireString());
    }

    [Theory]
    [InlineData("anonymous", EventReason.Anonymous)]
    [InlineData("invalid_token", EventReason.InvalidToken)]
    [InlineData("no_issuer_match", EventReason.NoIssuerMatch)]
    [InlineData("no_app_match", EventReason.NoAppMatch)]
    [InlineData("ambiguous_app", EventReason.AmbiguousApp)]
    [InlineData("no_profile_match", EventReason.NoProfileMatch)]
    [InlineData("grant_construction_failed", EventReason.GrantConstructionFailed)]
    [InlineData("granted", EventReason.Granted)]
    public void ParseWireString_RoundTripsFromDoc09Codes(string wireValue, EventReason expected)
    {
        Assert.Equal(expected, EventReasonWireFormat.ParseWireString(wireValue));
    }

    [Fact]
    public void ParseWireString_UnknownCode_Throws()
    {
        Assert.Throws<FormatException>(() => EventReasonWireFormat.ParseWireString("not-a-real-code"));
    }
}
