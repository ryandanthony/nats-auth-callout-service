// -----------------------------------------------------------------------
// <copyright file="TemplatesEndToEndTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Core.Claims;
using AuthCallout.Core.Templates;

namespace AuthCallout.Core.Tests.Templates;

/// <summary>
/// End-to-end golden test wiring <see cref="VariableResolver"/> and <see cref="TemplateExpander"/>
/// together from raw validated claims, exactly reproducing the doc 02 §Templated subject
/// variables / ADR 0009 worked example.
/// </summary>
public sealed class TemplatesEndToEndTests
{
    [Fact]
    public void ResolveAndExpand_DocExample_FromRawClaims_YieldsTwoDocumentedSubjects()
    {
        var claims = ClaimsAccessor.Parse(
            """{ "household_id": ["H1", "H2"], "email": "Alice@Example.com" }""");

        var variables = new[]
        {
            new TemplateVariable("tenant", "household_id", [], Required: false),
            new TemplateVariable(
                "user",
                "email",
                [Transform.Lowercase, Transform.ReplaceOf(".", "_"), Transform.ReplaceOf("@", "_")],
                Required: false),
        };

        var denyReason = VariableResolver.TryResolveAll(variables, claims, out var resolved);
        Assert.Null(denyReason);

        var publishSubjects = TemplateExpander.ExpandAll(["mise.house.{tenant}.user.{user}.commands"], resolved);
        var subscribeSubjects = TemplateExpander.ExpandAll(["mise.house.{tenant}.user.{user}.events.>"], resolved);

        Assert.Equal(
            ["mise.house.H1.user.alice_example_com.commands", "mise.house.H2.user.alice_example_com.commands"],
            publishSubjects);
        Assert.Equal(
            ["mise.house.H1.user.alice_example_com.events.>", "mise.house.H2.user.alice_example_com.events.>"],
            subscribeSubjects);
    }

    [Fact]
    public void ResolveAndExpand_RequiredVariableMissing_StopsBeforeExpansion()
    {
        var claims = ClaimsAccessor.Parse("""{ "email": "alice@example.com" }""");
        var variables = new[]
        {
            new TemplateVariable("tenant", "household_id", [], Required: true),
        };

        var denyReason = VariableResolver.TryResolveAll(variables, claims, out _);

        Assert.Equal(DenyReason.RequiredVariableMissing, denyReason);
    }
}
