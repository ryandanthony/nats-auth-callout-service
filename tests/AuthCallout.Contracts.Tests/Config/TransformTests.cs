// -----------------------------------------------------------------------
// <copyright file="TransformTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Contracts.Config;

namespace AuthCallout.Contracts.Tests.Config;

/// <summary>
/// Table tests for the transform-spec grammar parser (ADR 0009), pinned in
/// <c>AuthCallout.Contracts</c> per the P02 packet split (parser here, execution in
/// <c>AuthCallout.Core</c>/P04).
/// </summary>
public sealed class TransformTests
{
    [Theory]
    [InlineData("\"lowercase\"", TransformKind.Lowercase)]
    [InlineData("\"uppercase\"", TransformKind.Uppercase)]
    [InlineData("\"sanitize\"", TransformKind.Sanitize)]
    [InlineData("\"hash\"", TransformKind.Hash)]
    [InlineData("\"require-safe\"", TransformKind.RequireSafe)]
    public void Parse_NoArgumentTransforms_Succeed(string json, TransformKind expectedKind)
    {
        var element = JsonDocument.Parse(json).RootElement;

        var transform = Transform.Parse(element);

        Assert.Equal(expectedKind, transform.Kind);
    }

    [Fact]
    public void Parse_Replace_ParsesFromAndTo()
    {
        var element = JsonDocument.Parse("""{ "replace": { "from": ".", "to": "_" } }""").RootElement;

        var transform = Transform.Parse(element);

        Assert.Equal(TransformKind.Replace, transform.Kind);
        Assert.Equal(".", transform.From);
        Assert.Equal("_", transform.To);
    }

    [Fact]
    public void Parse_Prefix_ParsesText()
    {
        var element = JsonDocument.Parse("""{ "prefix": "tenant-" }""").RootElement;

        var transform = Transform.Parse(element);

        Assert.Equal(TransformKind.Prefix, transform.Kind);
        Assert.Equal("tenant-", transform.Text);
    }

    [Fact]
    public void Parse_Suffix_ParsesText()
    {
        var element = JsonDocument.Parse("""{ "suffix": "-suffix" }""").RootElement;

        var transform = Transform.Parse(element);

        Assert.Equal(TransformKind.Suffix, transform.Kind);
        Assert.Equal("-suffix", transform.Text);
    }

    [Theory]
    [InlineData("\"unknown-transform\"")]
    [InlineData("""{ "unknown": "x" }""")]
    [InlineData("""{ "replace": { "from": "" } }""")]
    [InlineData("""{ "replace": { "to": "x" } }""")]
    [InlineData("""{ "prefix": 5 }""")]
    [InlineData("""{ "replace": { "from": "x", "to": "y" }, "extra": 1 }""")]
    [InlineData("5")]
    public void Parse_MalformedOrUnknown_Throws(string json)
    {
        var element = JsonDocument.Parse(json).RootElement;

        Assert.Throws<FormatException>(() => Transform.Parse(element));
    }

    [Fact]
    public void TryParse_MalformedInput_ReturnsFalseWithError()
    {
        var element = JsonDocument.Parse("\"bogus\"").RootElement;

        var succeeded = Transform.TryParse(element, out _, out var error);

        Assert.False(succeeded);
        Assert.NotNull(error);
    }

    [Fact]
    public void ReplaceOf_RejectsEmptyFrom()
    {
        Assert.Throws<ArgumentException>(() => Transform.ReplaceOf(string.Empty, "_"));
    }

    [Fact]
    public void ReplaceOf_AllowsEmptyTo()
    {
        var transform = Transform.ReplaceOf(".", string.Empty);

        Assert.Equal(string.Empty, transform.To);
    }
}
