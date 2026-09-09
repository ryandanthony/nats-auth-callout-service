// -----------------------------------------------------------------------
// <copyright file="ClaimsAccessorTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Core.Claims;

namespace AuthCallout.Core.Tests.Claims;

/// <summary>
/// Table tests for <see cref="ClaimsAccessor"/>: nested path traversal, scalar/array/absent
/// distinction, and the pinned coercion rules (doc 02 §Claim match).
/// </summary>
public sealed class ClaimsAccessorTests
{
    [Fact]
    public void GetValues_TopLevelStringClaim_ReturnsSingleValue()
    {
        var accessor = Build("""{ "sub": "user-1" }""");

        Assert.Equal(["user-1"], accessor.GetValues("sub"));
    }

    [Fact]
    public void GetValues_NestedPath_TraversesObjects()
    {
        var accessor = Build("""{ "realm_access": { "roles": ["admin", "user"] } }""");

        Assert.Equal(["admin", "user"], accessor.GetValues("realm_access.roles"));
    }

    [Fact]
    public void GetValues_DeeplyNestedPath_Traverses()
    {
        var accessor = Build("""{ "a": { "b": { "c": "deep" } } }""");

        Assert.Equal(["deep"], accessor.GetValues("a.b.c"));
    }

    [Fact]
    public void GetValues_AbsentPath_ReturnsEmpty()
    {
        var accessor = Build("""{ "sub": "user-1" }""");

        Assert.Empty(accessor.GetValues("missing"));
    }

    [Fact]
    public void GetValues_AbsentNestedPath_ReturnsEmpty()
    {
        var accessor = Build("""{ "realm_access": { "roles": ["admin"] } }""");

        Assert.Empty(accessor.GetValues("realm_access.missing"));
    }

    [Fact]
    public void GetValues_PathThroughScalar_ReturnsEmpty()
    {
        // "sub" is a scalar, so "sub.roles" cannot descend further.
        var accessor = Build("""{ "sub": "user-1" }""");

        Assert.Empty(accessor.GetValues("sub.roles"));
    }

    [Fact]
    public void GetValues_PathThroughArray_ReturnsEmpty()
    {
        // "roles" is an array, so "roles.nested" cannot descend into it.
        var accessor = Build("""{ "roles": ["a", "b"] }""");

        Assert.Empty(accessor.GetValues("roles.nested"));
    }

    [Fact]
    public void GetValues_ObjectClaim_ContributesNoValue()
    {
        var accessor = Build("""{ "realm_access": { "roles": ["admin"] } }""");

        Assert.Empty(accessor.GetValues("realm_access"));
    }

    [Fact]
    public void GetValues_NullClaim_ContributesNoValue()
    {
        var accessor = Build("""{ "middle_name": null }""");

        Assert.Empty(accessor.GetValues("middle_name"));
    }

    [Theory]
    [InlineData("""{ "n": 42 }""", "42")]
    [InlineData("""{ "n": 3.14 }""", "3.14")]
    [InlineData("""{ "n": 0 }""", "0")]
    public void GetValues_NumberClaim_CoercedToInvariantString(string json, string expected)
    {
        var accessor = Build(json);

        Assert.Equal([expected], accessor.GetValues("n"));
    }

    [Theory]
    [InlineData("""{ "b": true }""", "true")]
    [InlineData("""{ "b": false }""", "false")]
    public void GetValues_BooleanClaim_CoercedToInvariantString(string json, string expected)
    {
        var accessor = Build(json);

        Assert.Equal([expected], accessor.GetValues("b"));
    }

    [Fact]
    public void GetValues_ArrayOfMixedScalars_CoercesEach()
    {
        var accessor = Build("""{ "mixed": ["x", 1, true, false] }""");

        Assert.Equal(["x", "1", "true", "false"], accessor.GetValues("mixed"));
    }

    [Fact]
    public void GetValues_ArrayContainingObjectOrNull_SkipsThoseElements()
    {
        var accessor = Build("""{ "mixed": ["a", { "k": "v" }, null, "b", [1, 2]] }""");

        Assert.Equal(["a", "b"], accessor.GetValues("mixed"));
    }

    [Fact]
    public void GetValues_EmptyArray_ReturnsEmpty()
    {
        var accessor = Build("""{ "roles": [] }""");

        Assert.Empty(accessor.GetValues("roles"));
    }

    [Fact]
    public void GetSingleValue_ScalarClaim_ReturnsFirstValue()
    {
        var accessor = Build("""{ "sub": "user-1" }""");

        Assert.Equal("user-1", accessor.GetSingleValue("sub"));
    }

    [Fact]
    public void GetSingleValue_ArrayClaim_ReturnsFirstElement()
    {
        var accessor = Build("""{ "roles": ["admin", "user"] }""");

        Assert.Equal("admin", accessor.GetSingleValue("roles"));
    }

    [Fact]
    public void GetSingleValue_AbsentPath_ReturnsNull()
    {
        var accessor = Build("""{ "sub": "user-1" }""");

        Assert.Null(accessor.GetSingleValue("missing"));
    }

    [Fact]
    public void HasClaim_PresentWithValue_ReturnsTrue()
    {
        var accessor = Build("""{ "sub": "user-1" }""");

        Assert.True(accessor.HasClaim("sub"));
    }

    [Fact]
    public void HasClaim_AbsentPath_ReturnsFalse()
    {
        var accessor = Build("""{ "sub": "user-1" }""");

        Assert.False(accessor.HasClaim("missing"));
    }

    [Fact]
    public void HasClaim_EmptyArray_ReturnsFalse()
    {
        var accessor = Build("""{ "roles": [] }""");

        Assert.False(accessor.HasClaim("roles"));
    }

    [Fact]
    public void HasClaim_ObjectClaim_ReturnsFalse()
    {
        var accessor = Build("""{ "realm_access": { "roles": ["admin"] } }""");

        Assert.False(accessor.HasClaim("realm_access"));
    }

    [Fact]
    public void Constructor_NonObjectRoot_Throws()
    {
        using var document = JsonDocument.Parse("[1,2,3]");

        Assert.Throws<ArgumentException>(() => new ClaimsAccessor(document.RootElement));
    }

    [Fact]
    public void Parse_ValidJsonObject_BuildsAccessor()
    {
        var accessor = ClaimsAccessor.Parse("""{ "sub": "user-1" }""");

        Assert.Equal("user-1", accessor.GetSingleValue("sub"));
    }

    [Fact]
    public void Parse_NonObjectRoot_Throws()
    {
        Assert.Throws<ArgumentException>(() => ClaimsAccessor.Parse("\"just a string\""));
    }

    [Fact]
    public void Parse_MalformedJson_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => ClaimsAccessor.Parse("{ not json"));
    }

    private static ClaimsAccessor Build(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new ClaimsAccessor(document.RootElement);
    }
}
