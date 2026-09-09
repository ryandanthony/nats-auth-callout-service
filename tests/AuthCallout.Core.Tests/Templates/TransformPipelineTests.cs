// -----------------------------------------------------------------------
// <copyright file="TransformPipelineTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Core.Templates;

namespace AuthCallout.Core.Tests.Templates;

/// <summary>
/// Per-transform table tests for <see cref="TransformPipeline"/> (ADR 0009 §Transform pipeline),
/// pinning the exact semantics documented on the type.
/// </summary>
public sealed class TransformPipelineTests
{
    [Fact]
    public void TryApply_Lowercase_LowercasesInvariantly()
    {
        var succeeded = TransformPipeline.TryApply([Transform.Lowercase], "AlicE", out var result);

        Assert.True(succeeded);
        Assert.Equal("alice", result);
    }

    [Fact]
    public void TryApply_Uppercase_UppercasesInvariantly()
    {
        var succeeded = TransformPipeline.TryApply([Transform.Uppercase], "alice", out var result);

        Assert.True(succeeded);
        Assert.Equal("ALICE", result);
    }

    [Theory]
    [InlineData("alice@example.com", ".", "_", "alice@example_com")]
    [InlineData("a.b.c", ".", "_", "a_b_c")]
    [InlineData("no-match", "z", "_", "no-match")]
    [InlineData("delete.me", ".", "", "deleteme")]
    public void TryApply_Replace_ReplacesEveryOccurrence(string input, string from, string to, string expected)
    {
        var succeeded = TransformPipeline.TryApply([Transform.ReplaceOf(from, to)], input, out var result);

        Assert.True(succeeded);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("alice@example.com", "alice_example_com")]
    [InlineData("a b\tc", "a_b_c")]
    [InlineData("subject.with.dots", "subject_with_dots")]
    [InlineData("wild*card>", "wild_card_")]
    [InlineData("already-safe_123", "already-safe_123")]
    public void TryApply_Sanitize_MapsUnsafeCharsToUnderscore(string input, string expected)
    {
        var succeeded = TransformPipeline.TryApply([Transform.Sanitize], input, out var result);

        Assert.True(succeeded);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryApply_Sanitize_EmptyString_RemainsEmpty()
    {
        var succeeded = TransformPipeline.TryApply([Transform.Sanitize], string.Empty, out var result);

        Assert.True(succeeded);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void TryApply_Hash_ProducesSixteenLowercaseHexChars()
    {
        var succeeded = TransformPipeline.TryApply([Transform.Hash], "alice@example.com", out var result);

        Assert.True(succeeded);
        Assert.Equal(16, result.Length);
        Assert.Matches("^[0-9a-f]{16}$", result);
    }

    [Fact]
    public void TryApply_Hash_IsStableAcrossCalls()
    {
        TransformPipeline.TryApply([Transform.Hash], "alice@example.com", out var first);
        TransformPipeline.TryApply([Transform.Hash], "alice@example.com", out var second);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TryApply_Hash_DifferentInputsProduceDifferentDigests()
    {
        TransformPipeline.TryApply([Transform.Hash], "alice@example.com", out var first);
        TransformPipeline.TryApply([Transform.Hash], "bob@example.com", out var second);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TryApply_Prefix_PrependsText()
    {
        var succeeded = TransformPipeline.TryApply([Transform.PrefixOf("tenant-")], "acme", out var result);

        Assert.True(succeeded);
        Assert.Equal("tenant-acme", result);
    }

    [Fact]
    public void TryApply_Suffix_AppendsText()
    {
        var succeeded = TransformPipeline.TryApply([Transform.SuffixOf("-suffix")], "acme", out var result);

        Assert.True(succeeded);
        Assert.Equal("acme-suffix", result);
    }

    [Theory]
    [InlineData("safe-value_123")]
    [InlineData("a")]
    [InlineData("A1_-")]
    public void TryApply_RequireSafe_SafeValue_Succeeds(string safeValue)
    {
        var succeeded = TransformPipeline.TryApply([Transform.RequireSafe], safeValue, out var result);

        Assert.True(succeeded);
        Assert.Equal(safeValue, result);
    }

    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("has space")]
    [InlineData("has.dot")]
    [InlineData("has*star")]
    [InlineData("has>gt")]
    [InlineData("")]
    public void TryApply_RequireSafe_UnsafeValue_Fails(string unsafeValue)
    {
        var succeeded = TransformPipeline.TryApply([Transform.RequireSafe], unsafeValue, out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void TryApply_RequireSafe_AfterSanitize_Succeeds()
    {
        // The ADR 0009 worked example: lowercase, replace ., replace @, require-safe.
        var transforms = new[]
        {
            Transform.Lowercase,
            Transform.ReplaceOf(".", "_"),
            Transform.ReplaceOf("@", "_"),
            Transform.RequireSafe,
        };

        var succeeded = TransformPipeline.TryApply(transforms, "Alice@Example.com", out var result);

        Assert.True(succeeded);
        Assert.Equal("alice_example_com", result);
    }

    [Fact]
    public void TryApply_StepsAppliedLeftToRight_OrderMatters()
    {
        // prefix then lowercase should lowercase the prefix too.
        var transforms = new[] { Transform.PrefixOf("TENANT-"), Transform.Lowercase };

        TransformPipeline.TryApply(transforms, "Acme", out var result);

        Assert.Equal("tenant-acme", result);
    }

    [Fact]
    public void TryApply_EmptyPipeline_ReturnsValueUnchanged()
    {
        var succeeded = TransformPipeline.TryApply([], "unchanged", out var result);

        Assert.True(succeeded);
        Assert.Equal("unchanged", result);
    }

    [Fact]
    public void TryApply_RequireSafeMidPipeline_ShortCircuitsRemainingSteps()
    {
        // require-safe fails on the raw value (contains '@'); the suffix step must not run.
        var transforms = new[] { Transform.RequireSafe, Transform.SuffixOf("-should-not-appear") };

        var succeeded = TransformPipeline.TryApply(transforms, "alice@example.com", out var result);

        Assert.False(succeeded);
        Assert.Equal("alice@example.com", result);
    }

    [Fact]
    public void TryApply_NullTransforms_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TransformPipeline.TryApply(null!, "value", out _));
    }

    [Fact]
    public void TryApply_NullValue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TransformPipeline.TryApply([], null!, out _));
    }

    [Theory]
    [InlineData("safe-token_1", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("has.dot", false)]
    public void IsSafeSubjectToken_TableOfValues(string value, bool expected)
    {
        Assert.Equal(expected, TransformPipeline.IsSafeSubjectToken(value));
    }

    [Fact]
    public void IsSafeSubjectToken_NullValue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TransformPipeline.IsSafeSubjectToken(null!));
    }
}
