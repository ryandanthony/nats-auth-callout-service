// -----------------------------------------------------------------------
// <copyright file="VariableResolverTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Core.Claims;
using AuthCallout.Core.Templates;

namespace AuthCallout.Core.Tests.Templates;

/// <summary>
/// Table tests for <see cref="VariableResolver"/>: scalar/multi-valued resolution,
/// required-missing denial, unsafe-value denial, and optional-missing no-op (ADR 0009).
/// </summary>
public sealed class VariableResolverTests
{
    [Fact]
    public void TryResolveOne_ScalarClaim_ResolvesSingleValue()
    {
        var claims = ClaimsAccessor.Parse("""{ "household_id": "H1" }""");
        var variable = new TemplateVariable("tenant", "household_id", [], Required: false);

        var denyReason = VariableResolver.TryResolveOne(variable, claims, out var values);

        Assert.Null(denyReason);
        Assert.Equal(["H1"], values);
    }

    [Fact]
    public void TryResolveOne_MultiValuedClaim_ResolvesEveryValue()
    {
        var claims = ClaimsAccessor.Parse("""{ "household_id": ["H1", "H2"] }""");
        var variable = new TemplateVariable("tenant", "household_id", [], Required: false);

        var denyReason = VariableResolver.TryResolveOne(variable, claims, out var values);

        Assert.Null(denyReason);
        Assert.Equal(["H1", "H2"], values);
    }

    [Fact]
    public void TryResolveOne_AppliesTransformsToEachValue()
    {
        var claims = ClaimsAccessor.Parse("""{ "email": "Alice@Example.com" }""");
        var variable = new TemplateVariable(
            "user",
            "email",
            [Transform.Lowercase, Transform.ReplaceOf(".", "_"), Transform.ReplaceOf("@", "_")],
            Required: false);

        var denyReason = VariableResolver.TryResolveOne(variable, claims, out var values);

        Assert.Null(denyReason);
        Assert.Equal(["alice_example_com"], values);
    }

    [Fact]
    public void TryResolveOne_RequiredVariable_ClaimAbsent_DeniesRequiredVariableMissing()
    {
        var claims = ClaimsAccessor.Parse("{}");
        var variable = new TemplateVariable("tenant", "household_id", [], Required: true);

        var denyReason = VariableResolver.TryResolveOne(variable, claims, out var values);

        Assert.Equal(DenyReason.RequiredVariableMissing, denyReason);
        Assert.Empty(values);
    }

    [Fact]
    public void TryResolveOne_OptionalVariable_ClaimAbsent_ResolvesToNoValuesWithoutDenial()
    {
        var claims = ClaimsAccessor.Parse("{}");
        var variable = new TemplateVariable("tenant", "household_id", [], Required: false);

        var denyReason = VariableResolver.TryResolveOne(variable, claims, out var values);

        Assert.Null(denyReason);
        Assert.Empty(values);
    }

    [Fact]
    public void TryResolveOne_RequireSafeFails_DeniesUnsafeTemplateValue()
    {
        var claims = ClaimsAccessor.Parse("""{ "email": "alice@example.com" }""");
        var variable = new TemplateVariable("user", "email", [Transform.RequireSafe], Required: false);

        var denyReason = VariableResolver.TryResolveOne(variable, claims, out var values);

        Assert.Equal(DenyReason.UnsafeTemplateValue, denyReason);
        Assert.Empty(values);
    }

    [Fact]
    public void TryResolveOne_RequireSafeFails_OnRequiredVariable_StillDeniesUnsafeNotMissing()
    {
        // An unsafe value is a distinct denial from "missing", even when required is set.
        var claims = ClaimsAccessor.Parse("""{ "email": "alice@example.com" }""");
        var variable = new TemplateVariable("user", "email", [Transform.RequireSafe], Required: true);

        var denyReason = VariableResolver.TryResolveOne(variable, claims, out _);

        Assert.Equal(DenyReason.UnsafeTemplateValue, denyReason);
    }

    [Fact]
    public void TryResolveOne_RequireSafeFailsOnOneOfSeveralValues_DeniesWholeVariable()
    {
        var claims = ClaimsAccessor.Parse("""{ "emails": ["safe-one", "unsafe@one"] }""");
        var variable = new TemplateVariable("user", "emails", [Transform.RequireSafe], Required: false);

        var denyReason = VariableResolver.TryResolveOne(variable, claims, out var values);

        Assert.Equal(DenyReason.UnsafeTemplateValue, denyReason);
        Assert.Empty(values);
    }

    [Fact]
    public void TryResolveOne_NullVariable_Throws()
    {
        var claims = ClaimsAccessor.Parse("{}");

        Assert.Throws<ArgumentNullException>(() => VariableResolver.TryResolveOne(null!, claims, out _));
    }

    [Fact]
    public void TryResolveOne_NullClaims_Throws()
    {
        var variable = new TemplateVariable("tenant", "household_id", [], Required: false);

        Assert.Throws<ArgumentNullException>(() => VariableResolver.TryResolveOne(variable, null!, out _));
    }

    [Fact]
    public void TryResolveAll_MultipleVariables_ResolvesEachIndependently()
    {
        var claims = ClaimsAccessor.Parse("""{ "household_id": ["H1", "H2"], "email": "Alice@Example.com" }""");
        var variables = new[]
        {
            new TemplateVariable("tenant", "household_id", [], Required: false),
            new TemplateVariable("user", "email", [Transform.Lowercase, Transform.ReplaceOf(".", "_"), Transform.ReplaceOf("@", "_")], Required: false),
        };

        var denyReason = VariableResolver.TryResolveAll(variables, claims, out var resolved);

        Assert.Null(denyReason);
        Assert.Equal(["H1", "H2"], resolved["tenant"]);
        Assert.Equal(["alice_example_com"], resolved["user"]);
    }

    [Fact]
    public void TryResolveAll_OneRequiredVariableMissing_StopsAndReturnsDenial()
    {
        var claims = ClaimsAccessor.Parse("""{ "email": "alice@example.com" }""");
        var variables = new[]
        {
            new TemplateVariable("tenant", "household_id", [], Required: true),
            new TemplateVariable("user", "email", [], Required: false),
        };

        var denyReason = VariableResolver.TryResolveAll(variables, claims, out _);

        Assert.Equal(DenyReason.RequiredVariableMissing, denyReason);
    }

    [Fact]
    public void TryResolveAll_EmptyVariableList_ResolvesToEmptyMap()
    {
        var claims = ClaimsAccessor.Parse("{}");

        var denyReason = VariableResolver.TryResolveAll([], claims, out var resolved);

        Assert.Null(denyReason);
        Assert.Empty(resolved);
    }

    [Fact]
    public void TryResolveAll_NullVariables_Throws()
    {
        var claims = ClaimsAccessor.Parse("{}");

        Assert.Throws<ArgumentNullException>(() => VariableResolver.TryResolveAll(null!, claims, out _));
    }

    [Fact]
    public void TryResolveAll_NullClaims_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => VariableResolver.TryResolveAll([], null!, out _));
    }
}
