// -----------------------------------------------------------------------
// <copyright file="StaticAndDelegateTokenProviderTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Tokens;

namespace AuthCallout.Client.Tests.Tokens;

/// <summary>Tests for <see cref="StaticTokenProvider"/> and <see cref="DelegateTokenProvider"/>.</summary>
public sealed class StaticAndDelegateTokenProviderTests
{
    [Fact]
    public async Task StaticTokenProvider_GetTokenAsync_ReturnsFixedToken()
    {
        var provider = new StaticTokenProvider("fixed-token");

        Assert.Equal("fixed-token", await provider.GetTokenAsync(CancellationToken.None));
        await provider.InvalidateAsync(CancellationToken.None);
        Assert.Equal("fixed-token", await provider.GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StaticTokenProvider_NullToken_ReturnsNull()
    {
        var provider = new StaticTokenProvider(null);

        Assert.Null(await provider.GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DelegateTokenProvider_GetTokenAsync_InvokesDelegate()
    {
        var calls = 0;
        var provider = new DelegateTokenProvider(_ =>
        {
            calls++;
            return ValueTask.FromResult<string?>("delegated-token");
        });

        var token = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("delegated-token", token);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DelegateTokenProvider_InvalidateAsync_InvokesInvalidateDelegate()
    {
        var invalidated = false;
        var provider = new DelegateTokenProvider(
            _ => ValueTask.FromResult<string?>("token"),
            _ =>
            {
                invalidated = true;
                return ValueTask.CompletedTask;
            });

        await provider.InvalidateAsync(CancellationToken.None);

        Assert.True(invalidated);
    }

    [Fact]
    public async Task DelegateTokenProvider_NoInvalidateDelegate_IsNoOp()
    {
        var provider = new DelegateTokenProvider(_ => ValueTask.FromResult<string?>("token"));

        await provider.InvalidateAsync(CancellationToken.None);

        // No exception: the omitted invalidate delegate is treated as a no-op.
        Assert.True(true);
    }
}
