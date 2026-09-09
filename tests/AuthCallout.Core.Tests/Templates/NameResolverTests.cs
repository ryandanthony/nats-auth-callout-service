// -----------------------------------------------------------------------
// <copyright file="NameResolverTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Decisions;
using AuthCallout.Core.Claims;
using AuthCallout.Core.Templates;

namespace AuthCallout.Core.Tests.Templates;

/// <summary>
/// Table tests for <see cref="NameResolver"/>: the <c>nameClaim</c> -&gt; <c>sub</c> fallback
/// chain (D19).
/// </summary>
public sealed class NameResolverTests
{
    [Fact]
    public void TryResolve_NameClaimPresent_ResolvesToNameClaimValue()
    {
        var claims = ClaimsAccessor.Parse("""{ "preferred_username": "alice", "sub": "user-1" }""");

        var denyReason = NameResolver.TryResolve("preferred_username", claims, out var name);

        Assert.Null(denyReason);
        Assert.Equal("alice", name);
    }

    [Fact]
    public void TryResolve_NameClaimAbsent_FallsBackToSub()
    {
        var claims = ClaimsAccessor.Parse("""{ "sub": "user-1" }""");

        var denyReason = NameResolver.TryResolve("preferred_username", claims, out var name);

        Assert.Null(denyReason);
        Assert.Equal("user-1", name);
    }

    [Fact]
    public void TryResolve_NeitherNameClaimNorSubPresent_DeniesNameClaimError()
    {
        var claims = ClaimsAccessor.Parse("""{ "email": "alice@example.com" }""");

        var denyReason = NameResolver.TryResolve("preferred_username", claims, out var name);

        Assert.Equal(DenyReason.NameClaimError, denyReason);
        Assert.Null(name);
    }

    [Fact]
    public void TryResolve_NameClaimIsSub_ResolvesDirectlyWithoutDoubleFallback()
    {
        var claims = ClaimsAccessor.Parse("""{ "sub": "user-1" }""");

        var denyReason = NameResolver.TryResolve("sub", claims, out var name);

        Assert.Null(denyReason);
        Assert.Equal("user-1", name);
    }

    [Fact]
    public void TryResolve_NameClaimResolvesToArray_UsesFirstValue()
    {
        var claims = ClaimsAccessor.Parse("""{ "roles": ["primary", "secondary"], "sub": "user-1" }""");

        var denyReason = NameResolver.TryResolve("roles", claims, out var name);

        Assert.Null(denyReason);
        Assert.Equal("primary", name);
    }

    [Fact]
    public void TryResolve_NullNameClaim_Throws()
    {
        var claims = ClaimsAccessor.Parse("{}");

        Assert.Throws<ArgumentNullException>(() => NameResolver.TryResolve(null!, claims, out _));
    }

    [Fact]
    public void TryResolve_NullClaims_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => NameResolver.TryResolve("sub", null!, out _));
    }
}
