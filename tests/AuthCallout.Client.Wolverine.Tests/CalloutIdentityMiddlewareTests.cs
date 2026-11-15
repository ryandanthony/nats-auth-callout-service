// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityMiddlewareTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Identity;
using AuthCallout.Client.Wolverine;
using AuthCallout.Contracts.Seams;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wolverine;

namespace AuthCallout.Client.Wolverine.Tests;

/// <summary>
/// Tests for <see cref="CalloutIdentityMiddleware"/> (ADR 0022 task 3): missing/forged/expired
/// header stops the chain; a valid header continues with a populated
/// <see cref="CalloutIdentityContext"/>.
/// </summary>
public sealed class CalloutIdentityMiddlewareTests
{
    [Fact]
    public async Task LoadAsync_MissingHeader_StopsWithNullIdentity()
    {
        var envelope = new Envelope { Data = [] };
        var verifierAccessor = Substitute.For<ICalloutIdentityVerifierAccessor>();

        var (continuation, identity) = await CalloutIdentityMiddleware.LoadAsync(envelope, verifierAccessor, NullLogger.Instance);

        Assert.Equal(HandlerContinuation.Stop, continuation);
        Assert.Null(identity);
        await verifierAccessor.DidNotReceive().GetVerifierAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_EmptyHeader_StopsWithNullIdentity()
    {
        var envelope = new Envelope { Data = [] };
        envelope.Headers[CalloutIdentityHeader.FallbackName] = string.Empty;
        var verifierAccessor = Substitute.For<ICalloutIdentityVerifierAccessor>();

        var (continuation, identity) = await CalloutIdentityMiddleware.LoadAsync(envelope, verifierAccessor, NullLogger.Instance);

        Assert.Equal(HandlerContinuation.Stop, continuation);
        Assert.Null(identity);
    }

    [Fact]
    public async Task LoadAsync_ForgedOrExpiredHeader_StopsWithNullIdentity()
    {
        var envelope = new Envelope { Data = [] };
        envelope.Headers[CalloutIdentityHeader.FallbackName] = "forged-or-expired-jwt";
        var verifier = Substitute.For<ICalloutIdentityVerifier>();
        verifier.VerifyAsync("forged-or-expired-jwt", Arg.Any<CancellationToken>()).Returns((AssertionClaims?)null);
        var verifierAccessor = Substitute.For<ICalloutIdentityVerifierAccessor>();
        verifierAccessor.GetVerifierAsync(Arg.Any<CancellationToken>()).Returns(verifier);

        var (continuation, identity) = await CalloutIdentityMiddleware.LoadAsync(envelope, verifierAccessor, NullLogger.Instance);

        Assert.Equal(HandlerContinuation.Stop, continuation);
        Assert.Null(identity);
    }

    [Fact]
    public async Task LoadAsync_ValidHeader_ContinuesWithPopulatedIdentity()
    {
        var envelope = new Envelope { Data = [] };
        envelope.Headers[CalloutIdentityHeader.FallbackName] = "valid-jwt";
        var claims = new AssertionClaims("conn-1", "sub-1", "notes", "https://id.example.com", DateTimeOffset.UtcNow.AddMinutes(5));
        var verifier = Substitute.For<ICalloutIdentityVerifier>();
        verifier.VerifyAsync("valid-jwt", Arg.Any<CancellationToken>()).Returns(claims);
        var verifierAccessor = Substitute.For<ICalloutIdentityVerifierAccessor>();
        verifierAccessor.GetVerifierAsync(Arg.Any<CancellationToken>()).Returns(verifier);

        var (continuation, identity) = await CalloutIdentityMiddleware.LoadAsync(envelope, verifierAccessor, NullLogger.Instance);

        Assert.Equal(HandlerContinuation.Continue, continuation);
        Assert.NotNull(identity);
        Assert.Equal("sub-1", identity!.Subject);
        Assert.Equal("notes", identity.Application);
        Assert.Equal("conn-1", identity.ConnectionId);
    }
}
