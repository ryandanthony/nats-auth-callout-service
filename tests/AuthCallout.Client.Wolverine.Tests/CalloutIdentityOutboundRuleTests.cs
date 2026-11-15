// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityOutboundRuleTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Identity;
using AuthCallout.Client.Wolverine;
using Wolverine;

namespace AuthCallout.Client.Wolverine.Tests;

/// <summary>
/// Tests for <see cref="CalloutIdentityOutboundRule"/> (ADR 0022 task 2): no-op when the
/// <see cref="OutboundIdentityHeaderHolder"/> has no cached value (service unset, or identity
/// propagation disabled so nothing was cached), attach when it has.
/// </summary>
public sealed class CalloutIdentityOutboundRuleTests
{
    [Fact]
    public void Modify_HolderServiceUnset_LeavesHeadersUntouched()
    {
        var rule = new CalloutIdentityOutboundRule(new OutboundIdentityHeaderHolder());
        var envelope = new Envelope { Data = [] };

        rule.Modify(envelope);

        Assert.Empty(envelope.Headers);
    }

    [Fact]
    public void Modify_NoCachedHeaderValue_LeavesHeadersUntouched()
    {
        var service = new OutboundIdentityHeaderService(EmptyServiceProvider.Instance);
        var holder = new OutboundIdentityHeaderHolder { Service = service };
        var rule = new CalloutIdentityOutboundRule(holder);
        var envelope = new Envelope { Data = [] };

        rule.Modify(envelope);

        Assert.False(envelope.Headers.ContainsKey(CalloutIdentityHeader.FallbackName));
    }

    [Fact]
    public void Modify_CachedHeaderValue_AttachesHeaderToEnvelope()
    {
        var service = new OutboundIdentityHeaderService(EmptyServiceProvider.Instance);
        service.SetCachedHeaderValueForTests("compact-jwt");
        var holder = new OutboundIdentityHeaderHolder { Service = service };
        var rule = new CalloutIdentityOutboundRule(holder);
        var envelope = new Envelope { Data = [] };

        rule.Modify(envelope);

        Assert.Equal("compact-jwt", envelope.Headers[CalloutIdentityHeader.FallbackName]);
    }

    [Fact]
    public void Modify_NullEnvelope_Throws()
    {
        var rule = new CalloutIdentityOutboundRule(new OutboundIdentityHeaderHolder());

        Assert.Throws<ArgumentNullException>(() => rule.Modify(null!));
    }

    /// <summary>A minimal <see cref="IServiceProvider"/> for constructing <see cref="OutboundIdentityHeaderService"/> in tests that never resolve services.</summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
