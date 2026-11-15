// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityPolicyTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Wolverine;
using Wolverine.Runtime.Handlers;

namespace AuthCallout.Client.Wolverine.Tests;

/// <summary>
/// Tests for <see cref="CalloutIdentityPolicy.AppliesTo"/> (ADR 0022 open question 1: policy
/// with opt-out default — every handler chain gets <see cref="CalloutIdentityMiddleware"/>
/// unless its message type carries <c>[RequireIdentity(false)]</c>).
/// </summary>
public sealed class CalloutIdentityPolicyTests
{
    [Fact]
    public void AppliesTo_NoAttribute_ReturnsTrue()
    {
        var chain = new HandlerChain(typeof(PlainMessage), new HandlerGraph());

        Assert.True(CalloutIdentityPolicy.AppliesTo(chain));
    }

    [Fact]
    public void AppliesTo_RequireIdentityFalse_ReturnsFalse()
    {
        var chain = new HandlerChain(typeof(ExemptMessage), new HandlerGraph());

        Assert.False(CalloutIdentityPolicy.AppliesTo(chain));
    }

    [Fact]
    public void AppliesTo_RequireIdentityTrueExplicit_ReturnsTrue()
    {
        var chain = new HandlerChain(typeof(ExplicitlyRequiredMessage), new HandlerGraph());

        Assert.True(CalloutIdentityPolicy.AppliesTo(chain));
    }

    [Fact]
    public void AppliesTo_NullChain_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CalloutIdentityPolicy.AppliesTo(null!));
    }

    private sealed record PlainMessage;

    [RequireIdentity(false)]
    private sealed record ExemptMessage;

    [RequireIdentity(true)]
    private sealed record ExplicitlyRequiredMessage;
}
