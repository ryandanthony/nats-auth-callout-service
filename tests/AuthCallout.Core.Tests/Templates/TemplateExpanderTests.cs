// -----------------------------------------------------------------------
// <copyright file="TemplateExpanderTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Core.Templates;

namespace AuthCallout.Core.Tests.Templates;

/// <summary>
/// Table tests for <see cref="TemplateExpander"/>: cross-product expansion, no-value handling,
/// and the ADR 0009 / doc 02 worked example (golden test).
/// </summary>
public sealed class TemplateExpanderTests
{
    [Fact]
    public void Expand_NoPlaceholders_ReturnsTemplateUnchanged()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        var subjects = TemplateExpander.Expand("notes.static.subject", resolved);

        Assert.Equal(["notes.static.subject"], subjects);
    }

    [Fact]
    public void Expand_SingleValuedVariable_SubstitutesOnce()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["tenant"] = ["H1"],
        };

        var subjects = TemplateExpander.Expand("house.{tenant}.commands", resolved);

        Assert.Equal(["house.H1.commands"], subjects);
    }

    [Fact]
    public void Expand_MultiValuedVariable_ExpandsOncePerValue()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["tenant"] = ["H1", "H2"],
        };

        var subjects = TemplateExpander.Expand("house.{tenant}.commands", resolved);

        Assert.Equal(["house.H1.commands", "house.H2.commands"], subjects);
    }

    [Fact]
    public void Expand_MultipleMultiValuedVariables_ExpandsOverCrossProduct()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["tenant"] = ["H1", "H2"],
            ["role"] = ["admin", "member"],
        };

        var subjects = TemplateExpander.Expand("house.{tenant}.{role}.commands", resolved);

        Assert.Equal(
            [
                "house.H1.admin.commands",
                "house.H1.member.commands",
                "house.H2.admin.commands",
                "house.H2.member.commands",
            ],
            subjects);
    }

    [Fact]
    public void Expand_ThreeMultiValuedVariables_ExpandsOverFullCrossProduct()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["a"] = ["1", "2"],
            ["b"] = ["x", "y"],
            ["c"] = ["p", "q"],
        };

        var subjects = TemplateExpander.Expand("{a}.{b}.{c}", resolved);

        Assert.Equal(8, subjects.Count);
        Assert.Contains("1.x.p", subjects);
        Assert.Contains("2.y.q", subjects);
    }

    [Fact]
    public void Expand_VariableWithNoValue_EmitsNothing()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["tenant"] = [],
        };

        var subjects = TemplateExpander.Expand("house.{tenant}.commands", resolved);

        Assert.Empty(subjects);
    }

    [Fact]
    public void Expand_OneOfSeveralVariablesHasNoValue_WholeTemplateEmitsNothing()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["tenant"] = ["H1"],
            ["role"] = [],
        };

        var subjects = TemplateExpander.Expand("house.{tenant}.{role}.commands", resolved);

        Assert.Empty(subjects);
    }

    [Fact]
    public void Expand_SamePlaceholderTwiceInOneTemplate_SubstitutesBothOccurrences()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["tenant"] = ["H1"],
        };

        var subjects = TemplateExpander.Expand("house.{tenant}.audit.{tenant}", resolved);

        Assert.Equal(["house.H1.audit.H1"], subjects);
    }

    [Fact]
    public void Expand_ResultIsSortedOrdinally()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["tenant"] = ["Z1", "A1", "M1"],
        };

        var subjects = TemplateExpander.Expand("house.{tenant}", resolved);

        Assert.Equal(["house.A1", "house.M1", "house.Z1"], subjects);
    }

    [Fact]
    public void Expand_UndeclaredVariable_ThrowsKeyNotFound()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        Assert.Throws<KeyNotFoundException>(() => TemplateExpander.Expand("house.{unknown}", resolved));
    }

    [Fact]
    public void Expand_NullTemplate_Throws()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        Assert.Throws<ArgumentNullException>(() => TemplateExpander.Expand(null!, resolved));
    }

    [Fact]
    public void Expand_NullResolvedVariables_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TemplateExpander.Expand("template", null!));
    }

    [Fact]
    public void ExpandAll_MultipleTemplates_FlattensAndSorts()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["tenant"] = ["H2", "H1"],
        };

        var subjects = TemplateExpander.ExpandAll(["house.{tenant}.commands", "house.{tenant}.events"], resolved);

        Assert.Equal(
            ["house.H1.commands", "house.H1.events", "house.H2.commands", "house.H2.events"],
            subjects);
    }

    [Fact]
    public void ExpandAll_DuplicateSubjectsAcrossTemplates_DeduplicatesResult()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["tenant"] = ["H1"],
        };

        var subjects = TemplateExpander.ExpandAll(["house.{tenant}.commands", "house.{tenant}.commands"], resolved);

        Assert.Equal(["house.H1.commands"], subjects);
    }

    [Fact]
    public void ExpandAll_EmptyTemplateList_ReturnsEmpty()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        var subjects = TemplateExpander.ExpandAll([], resolved);

        Assert.Empty(subjects);
    }

    [Fact]
    public void ExpandAll_NullTemplates_Throws()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        Assert.Throws<ArgumentNullException>(() => TemplateExpander.ExpandAll(null!, resolved));
    }

    [Fact]
    public void ExpandAll_NullResolvedVariables_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TemplateExpander.ExpandAll([], null!));
    }

    /// <summary>
    /// The doc 02 / ADR 0009 worked example, end to end: variables <c>tenant := household_id</c>
    /// and <c>user := email | lowercase | replace(. -&gt; _) | replace(@ -&gt; _)</c>, template
    /// <c>mise.house.{tenant}.user.{user}.commands</c>, token with
    /// <c>household_id = [H1, H2]</c> and <c>email = Alice@Example.com</c> yields exactly the
    /// two documented subjects.
    /// </summary>
    [Fact]
    public void Expand_DocExample_HouseholdAndUser_YieldsTwoDocumentedSubjects()
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["tenant"] = ["H1", "H2"],
            ["user"] = ["alice_example_com"], // pre-transformed, as VariableResolver would produce
        };

        var subjects = TemplateExpander.Expand("mise.house.{tenant}.user.{user}.commands", resolved);

        Assert.Equal(
            ["mise.house.H1.user.alice_example_com.commands", "mise.house.H2.user.alice_example_com.commands"],
            subjects);
    }
}
