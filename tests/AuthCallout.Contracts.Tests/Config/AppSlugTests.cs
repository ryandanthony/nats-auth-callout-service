// -----------------------------------------------------------------------
// <copyright file="AppSlugTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;

namespace AuthCallout.Contracts.Tests.Config;

/// <summary>
/// Golden tests for ADR 0013's slugification rule and physical bucket/key/subject
/// derivations — the single source of truth every consumer of a bucket name must agree
/// with byte-for-byte.
/// </summary>
public sealed class AppSlugTests
{
    [Theory]
    [InlineData("notes", "notes")]
    [InlineData("Notes", "notes")]
    [InlineData("NOTES", "notes")]
    [InlineData("Notes App", "notes-app")]
    [InlineData("notes_app", "notes-app")]
    [InlineData("notes.app.v2", "notes-app-v2")]
    [InlineData("  notes  ", "notes")]
    [InlineData("--notes--", "notes")]
    [InlineData("notes---app", "notes-app")]
    [InlineData("notes123", "notes123")]
    [InlineData("café", "caf")]
    public void From_SlugifiesPerAdr0013(string applicationName, string expectedSlug)
    {
        var slug = AppSlug.From(applicationName);

        Assert.Equal(expectedSlug, slug.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("...")]
    public void From_EmptyOrAllUnsafe_Throws(string applicationName)
    {
        Assert.Throws<FormatException>(() => AppSlug.From(applicationName));
    }

    [Fact]
    public void From_ExceedsMaxLength_Throws()
    {
        var tooLong = new string('a', AppSlug.MaxLength + 1);

        Assert.Throws<FormatException>(() => AppSlug.From(tooLong));
    }

    [Fact]
    public void From_AtMaxLength_Succeeds()
    {
        var exact = new string('a', AppSlug.MaxLength);

        var slug = AppSlug.From(exact);

        Assert.Equal(exact, slug.Value);
    }

    [Fact]
    public void TryFrom_NullApplicationName_Fails()
    {
        var succeeded = AppSlug.TryFrom(null, out _, out var error);

        Assert.False(succeeded);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("notes", "callout_apps_notes_oidc_tokens")]
    [InlineData("Notes App", "callout_apps_notes-app_oidc_tokens")]
    public void OidcTokensBucket_MatchesAdr0013MappingTable(string applicationName, string expectedBucket)
    {
        var slug = AppSlug.From(applicationName);

        Assert.Equal(expectedBucket, slug.OidcTokensBucket);
    }

    [Theory]
    [InlineData("notes", "callout_apps_notes_assertions")]
    public void AssertionsBucket_MatchesAdr0013MappingTable(string applicationName, string expectedBucket)
    {
        var slug = AppSlug.From(applicationName);

        Assert.Equal(expectedBucket, slug.AssertionsBucket);
    }

    [Theory]
    [InlineData("notes", "callout_apps_notes_discovery")]
    public void DiscoveryBucket_MatchesAdr0013MappingTable(string applicationName, string expectedBucket)
    {
        var slug = AppSlug.From(applicationName);

        Assert.Equal(expectedBucket, slug.DiscoveryBucket);
    }

    [Fact]
    public void AssertionKeysBucket_IsGlobalAndFixed()
    {
        Assert.Equal("callout_assertion_keys", AppSlug.AssertionKeysBucket);
    }

    [Fact]
    public void AssertionKeysCurrentKey_IsReservedAndFixed()
    {
        Assert.Equal("current", AppSlug.AssertionKeysCurrentKey);
        Assert.Equal(AppSlug.AssertionKeysCurrentKey, AssertionKey.ReservedCurrentKid);
    }

    [Fact]
    public void DiscoveryDescriptorKey_IsFixed()
    {
        Assert.Equal("descriptor", AppSlug.DiscoveryDescriptorKey);
    }

    [Fact]
    public void GrantSubjects_MatchAdr0013PhysicalMapping()
    {
        var slug = AppSlug.From("notes");

        Assert.Equal("$KV.callout_apps_notes_oidc_tokens.>", slug.OidcTokensReadSubject);
        Assert.Equal("$KV.callout_apps_notes_assertions.abc123", slug.AssertionSelfLookupSubject("abc123"));
        Assert.Equal("$KV.callout_assertion_keys.>", AppSlug.AssertionKeysReadSubject);
        Assert.Equal("$KV.callout_apps_notes_discovery.descriptor", slug.DiscoveryReadSubject);
    }

    [Fact]
    public void Equality_IsOrdinalAndValueBased()
    {
        var left = AppSlug.From("Notes");
        var right = AppSlug.From("notes");

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsSlugText()
    {
        var slug = AppSlug.From("Notes App");

        Assert.Equal("notes-app", slug.ToString());
    }
}
