// -----------------------------------------------------------------------
// <copyright file="DurationStringTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Contracts.Tests.Config;

/// <summary>Table tests for the <see cref="DurationString"/> parse grammar and round-trip formatting.</summary>
public sealed class DurationStringTests
{
    [Theory]
    [InlineData("60s", 60)]
    [InlineData("5m", 300)]
    [InlineData("1h", 3600)]
    [InlineData("1500ms", 1.5)]
    [InlineData("0s", 0)]
    public void Parse_ValidGrammar_ProducesExpectedTimeSpan(string text, double expectedSeconds)
    {
        var duration = DurationString.Parse(text);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), duration.Value);
    }

    [Theory]
    [InlineData("60")]
    [InlineData("s")]
    [InlineData("-5s")]
    [InlineData("5x")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("5.5s")]
    public void Parse_InvalidGrammar_Throws(string text)
    {
        Assert.Throws<FormatException>(() => DurationString.Parse(text));
    }

    [Fact]
    public void TryParse_Null_ReturnsFalse()
    {
        var succeeded = DurationString.TryParse(null, out _);

        Assert.False(succeeded);
    }

    [Theory]
    [InlineData("60s", "60s")]
    [InlineData("5m", "5m")]
    [InlineData("1h", "1h")]
    [InlineData("1500ms", "1500ms")]
    public void Parse_PreservesOriginalTextVerbatim(string input, string expectedText)
    {
        var duration = DurationString.Parse(input);

        Assert.Equal(expectedText, duration.Text);
    }

    [Theory]
    [InlineData(45, "45s")]
    [InlineData(300, "5m")]
    [InlineData(3600, "1h")]
    public void FromTimeSpan_FormatsCanonicalText(double seconds, string expectedText)
    {
        var duration = DurationString.FromTimeSpan(TimeSpan.FromSeconds(seconds));

        Assert.Equal(expectedText, duration.Text);
    }

    [Fact]
    public void FromTimeSpan_SubSecond_FormatsAsMilliseconds()
    {
        var duration = DurationString.FromTimeSpan(TimeSpan.FromMilliseconds(1500));

        Assert.Equal("1500ms", duration.Text);
    }

    [Fact]
    public void FromTimeSpan_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DurationString.FromTimeSpan(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Equality_IsValueBasedOnTimeSpan()
    {
        var a = DurationString.Parse("60s");
        var b = DurationString.Parse("60s");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsText()
    {
        var duration = DurationString.Parse("5m");

        Assert.Equal("5m", duration.ToString());
    }
}
