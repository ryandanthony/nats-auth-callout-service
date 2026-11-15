// -----------------------------------------------------------------------
// <copyright file="AuthViolationDetectorTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;

namespace AuthCallout.Client.Tests.Connection;

/// <summary>Tests for <see cref="AuthViolationDetector"/>'s inner-exception walk (ADR 0021 rule 2).</summary>
public sealed class AuthViolationDetectorTests
{
    [Fact]
    public void IsAuthorizationViolation_BareException_DetectsMarker()
    {
        var ex = new InvalidOperationException("Server error: Authorization Violation");

        Assert.True(AuthViolationDetector.IsAuthorizationViolation(ex));
    }

    [Fact]
    public void IsAuthorizationViolation_CaseInsensitive_DetectsMarker()
    {
        var ex = new InvalidOperationException("authorization violation occurred");

        Assert.True(AuthViolationDetector.IsAuthorizationViolation(ex));
    }

    [Fact]
    public void IsAuthorizationViolation_WrappedInInnerException_DetectsMarker()
    {
        var inner = new InvalidOperationException("Authorization Violation");
        var outer = new Exception("connect failed", inner);

        Assert.True(AuthViolationDetector.IsAuthorizationViolation(outer));
    }

    [Fact]
    public void IsAuthorizationViolation_WrappedInAggregateException_DetectsMarker()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("transport blip"),
            new InvalidOperationException("Authorization Violation"));

        Assert.True(AuthViolationDetector.IsAuthorizationViolation(aggregate));
    }

    [Fact]
    public void IsAuthorizationViolation_NoMarkerAnywhere_ReturnsFalse()
    {
        var ex = new InvalidOperationException("connection refused", new Exception("network unreachable"));

        Assert.False(AuthViolationDetector.IsAuthorizationViolation(ex));
    }

    [Fact]
    public void IsAuthorizationViolation_NullException_ReturnsFalse()
    {
        Assert.False(AuthViolationDetector.IsAuthorizationViolation(null));
    }
}
