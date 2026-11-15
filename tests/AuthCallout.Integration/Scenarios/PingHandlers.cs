// -----------------------------------------------------------------------
// <copyright file="PingHandlers.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Identity;
using Wolverine.Attributes;

namespace AuthCallout.Integration.Scenarios;

/// <summary>
/// Wolverine handlers for the P20 test suite's message types (<see cref="Ping"/>,
/// <see cref="AnonymousPing"/>). Every scenario file in this group drives a fresh in-test
/// <c>Host.CreateApplicationBuilder</c> instance wired onto these same static handlers (Wolverine
/// discovers handlers by scanning <c>Assembly.GetEntryAssembly()</c>/referenced assemblies for
/// the running test host's build; a single shared static class avoids every scenario needing
/// its own distinctly-named handler type purely to dodge Wolverine's message-type-name
/// collision). <see cref="Recorder"/> is the one static piece of mutable state here — every
/// test distinguishes its own traffic with a GUID folded into the message payload (see each
/// scenario's own doc comment) rather than resetting it, so tests remain safe to run in
/// parallel (xUnit's default within a test collection is parallel across classes).
/// </summary>
/// <remarks>
/// <c>[WolverineHandler]</c> is required because Wolverine's conventional discovery only
/// recognizes types named exactly <c>*Handler</c>/<c>*Consumer</c> (singular) — this type is
/// named <c>PingHandlers</c> (plural, matching this file's Handle-methods-for-everything shape)
/// and would otherwise be silently skipped, logging a warning rather than failing the build.
/// </remarks>
[WolverineHandler]
public static class PingHandlers
{
    /// <summary>Handles <see cref="Ping"/>: records the verified caller's identity and replies with <see cref="Pong"/>.</summary>
    /// <param name="message">The request.</param>
    /// <param name="identity">The verified caller identity (from <see cref="AuthCallout.Client.Wolverine.CalloutIdentityMiddleware"/>).</param>
    /// <returns>The reply.</returns>
    public static Pong Handle(Ping message, CalloutIdentityContext identity)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(identity);
        Recorder.PingHandled.Add((message.Payload, identity.Subject));
        return new Pong(message.Payload, identity.Subject);
    }

    /// <summary>Handles <see cref="AnonymousPing"/> (opted out of identity verification via <see cref="RequireIdentityAttribute"/>): records that the handler ran, with no identity parameter to inject.</summary>
    /// <param name="message">The anonymous request.</param>
    public static void Handle(AnonymousPing message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Recorder.AnonymousPingHandled.Add(message.Payload);
    }
}
