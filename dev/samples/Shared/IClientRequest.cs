// -----------------------------------------------------------------------
// <copyright file="IClientRequest.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// Marker for a message a <b>client</b> sends to a service and that the service must
/// authenticate per-message (a command or a query). <see cref="CalloutIdentityMiddleware"/>
/// is applied only to handlers whose message implements this interface
/// (<c>Policies.ForMessagesOfType&lt;IClientRequest&gt;()</c>), so internally-cascaded domain
/// events — which the service publishes to itself and which carry no <c>Callout-Identity</c>
/// header — are not subjected to (and rejected by) the identity check. A deliberately empty
/// marker interface is the idiomatic way to group message types for a Wolverine
/// <c>ForMessagesOfType&lt;T&gt;</c> policy, so the "empty interface" analyzers are suppressed
/// here rather than forcing a synthetic member.
/// </summary>
#pragma warning disable CA1040, S4023 // Marker interface: intentionally empty, used as a Wolverine message-policy grouping type.
public interface IClientRequest;
#pragma warning restore CA1040, S4023
