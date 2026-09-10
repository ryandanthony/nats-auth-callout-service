// -----------------------------------------------------------------------
// <copyright file="UnvalidatedIssuerReaderTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Core.Pipeline;

namespace AuthCallout.Core.Tests.Pipeline;

/// <summary>
/// Table tests for <see cref="UnvalidatedIssuerReader"/>: successful extraction plus every
/// malformed-input shape it must fail closed on without throwing (doc 03 Stage 1).
/// </summary>
public sealed class UnvalidatedIssuerReaderTests
{
    [Fact]
    public void TryReadIssuer_WellFormedToken_ReturnsIssuer()
    {
        var token = PipelineTestBuilders.RawToken(issuer: "https://issuer.example.com");

        var issuer = UnvalidatedIssuerReader.TryReadIssuer(token);

        Assert.Equal("https://issuer.example.com", issuer);
    }

    [Fact]
    public void TryReadIssuer_MissingIssClaim_ReturnsNull()
    {
        var token = PipelineTestBuilders.RawToken(issuer: null);

        var issuer = UnvalidatedIssuerReader.TryReadIssuer(token);

        Assert.Null(issuer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("only.one.dot.too.many")]
    public void TryReadIssuer_WrongSegmentShape_ReturnsNullWithoutThrowing(string malformed)
    {
        var issuer = UnvalidatedIssuerReader.TryReadIssuer(malformed);

        Assert.Null(issuer);
    }

    [Fact]
    public void TryReadIssuer_PayloadNotValidBase64Url_ReturnsNullWithoutThrowing()
    {
        var issuer = UnvalidatedIssuerReader.TryReadIssuer("header.!!!not-base64!!!.signature");

        Assert.Null(issuer);
    }

    [Fact]
    public void TryReadIssuer_PayloadNotValidJson_ReturnsNullWithoutThrowing()
    {
        var notJson = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not json"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var issuer = UnvalidatedIssuerReader.TryReadIssuer($"header.{notJson}.signature");

        Assert.Null(issuer);
    }

    [Fact]
    public void TryReadIssuer_PayloadIsJsonArray_ReturnsNullWithoutThrowing()
    {
        var arrayPayload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("[1,2,3]"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var issuer = UnvalidatedIssuerReader.TryReadIssuer($"header.{arrayPayload}.signature");

        Assert.Null(issuer);
    }

    [Fact]
    public void TryReadIssuer_IssClaimIsNotAString_ReturnsNull()
    {
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("""{"iss":123}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var issuer = UnvalidatedIssuerReader.TryReadIssuer($"header.{payload}.signature");

        Assert.Null(issuer);
    }
}
