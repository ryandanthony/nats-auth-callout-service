// -----------------------------------------------------------------------
// <copyright file="ActiveConfigSourceTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Host.Lifecycle;

namespace AuthCallout.Host.Tests.Lifecycle;

/// <summary>
/// Unit tests for <see cref="ActiveConfigSource"/>: doc 08 §Config reload → in-memory swap
/// ("a single immutable snapshot behind a volatile reference... In-flight requests keep
/// their snapshot; new requests read the new one").
/// </summary>
public sealed class ActiveConfigSourceTests
{
    [Fact]
    public void Current_is_null_before_the_first_apply()
    {
        var sut = new ActiveConfigSource();
        Assert.Null(sut.Current);
    }

    [Fact]
    public void Apply_publishes_the_new_snapshot_as_Current()
    {
        var sut = new ActiveConfigSource();
        var snapshot = HostTestBuilders.ActiveConfigFor(version: 1);

        sut.Apply(snapshot);

        Assert.Same(snapshot, sut.Current);
    }

    [Fact]
    public void An_in_flight_read_keeps_its_own_snapshot_reference_after_a_later_swap()
    {
        var sut = new ActiveConfigSource();
        var first = HostTestBuilders.ActiveConfigFor(version: 1);
        sut.Apply(first);

        var readDuringFlight = sut.Current; // simulates a resolution reading Current once per request.

        var second = HostTestBuilders.ActiveConfigFor(version: 2);
        sut.Apply(second);

        Assert.Same(first, readDuringFlight);
        Assert.Same(second, sut.Current);
    }

    [Fact]
    public void Apply_raises_Changed_with_the_new_snapshot()
    {
        var sut = new ActiveConfigSource();
        var snapshot = HostTestBuilders.ActiveConfigFor(version: 1);
        AuthCallout.Contracts.Config.ActiveConfig? seen = null;
        sut.Changed += (_, e) => seen = e.NewConfig;

        sut.Apply(snapshot);

        Assert.Same(snapshot, seen);
    }

    [Fact]
    public void Apply_throws_on_null()
    {
        var sut = new ActiveConfigSource();
        Assert.Throws<ArgumentNullException>(() => sut.Apply(null!));
    }
}
