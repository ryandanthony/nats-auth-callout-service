// -----------------------------------------------------------------------
// <copyright file="AnonymousComposer.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;

namespace AuthCallout.Core.Pipeline;

/// <summary>
/// Composes the anonymous grant (doc 03 Stage 0, ADR 0004): the de-duplicated union of every
/// configured application's optional <see cref="Application.AnonymousProfile"/> contribution.
/// An application that omits it contributes nothing (opt-out). Never reads or grants any KV
/// subject (doc 03 §Grant construction: "no KV reads ever" for anonymous; doc 07: "Anonymous
/// connections select no application and get no token record or assertion").
/// </summary>
internal static class AnonymousComposer
{
    /// <summary>
    /// Composes the anonymous union grant across every application in <paramref name="routing"/>.
    /// </summary>
    /// <param name="routing">The active routing aggregate.</param>
    /// <param name="anonymous">The global anonymous config (target account, expiry).</param>
    /// <param name="now">The current time, used to compute the anonymous grant's absolute expiry from <see cref="AnonymousGlobalConfig.Expiry"/> (test seam; the pipeline supplies <see cref="TimeProvider.GetUtcNow"/>).</param>
    /// <returns>
    /// The composed <see cref="GrantedDecision"/>, or <see cref="DenyReason.AnonymousNoContributions"/>
    /// when the union has zero subjects across every application (D15: "zero
    /// anonymousProfile contributions = deny").
    /// </returns>
    internal static AnonymousComposeResult Compose(RoutingConfig routing, AnonymousGlobalConfig anonymous, DateTimeOffset now)
    {
        var publishSeen = new HashSet<string>(StringComparer.Ordinal);
        var subscribeSeen = new HashSet<string>(StringComparer.Ordinal);
        var publish = new List<string>();
        var subscribe = new List<string>();

        foreach (var application in routing.Applications)
        {
            if (application.AnonymousProfile is not { } contribution)
            {
                continue;
            }

            SubjectListBuilder.AddRange(publish, publishSeen, contribution.Publish);
            SubjectListBuilder.AddRange(subscribe, subscribeSeen, contribution.Subscribe);
        }

        if (publish.Count == 0 && subscribe.Count == 0)
        {
            return AnonymousComposeResult.Deny(DenyReason.AnonymousNoContributions);
        }

        var decision = new GrantedDecision(
            Application: null,
            Principal: null,
            Issuer: null,
            TargetAccount: anonymous.TargetAccount,
            Name: AnonymousName,
            Expiry: now + anonymous.Expiry.Value,
            Publish: publish,
            Subscribe: subscribe,
            ProfileName: null,
            IdentityPropagationEnabled: false,
            SelfLookupKey: null);
        return AnonymousComposeResult.Success(decision);
    }

    /// <summary>Gets the fixed minted <c>name</c> for an anonymous grant (there is no claim to name it from).</summary>
    /// <value>The fixed anonymous JWT name, <c>"anonymous"</c>.</value>
    internal static string AnonymousName => "anonymous";

    /// <summary>The outcome of <see cref="Compose"/>: either the composed grant, or the empty-union denial.</summary>
    internal readonly record struct AnonymousComposeResult
    {
        private AnonymousComposeResult(GrantedDecision? decision, DenyReason? denyReason)
        {
            Decision = decision;
            DenyReason = denyReason;
        }

        /// <summary>Gets the composed grant, present only when <see cref="DenyReason"/> is null.</summary>
        /// <value>The composed grant, or null on denial.</value>
        internal GrantedDecision? Decision { get; }

        /// <summary>Gets the denial reason, present only when <see cref="Decision"/> is null.</summary>
        /// <value>The denial reason, or null on success.</value>
        internal DenyReason? DenyReason { get; }

        /// <summary>Creates a successful compose result.</summary>
        internal static AnonymousComposeResult Success(GrantedDecision decision) => new(decision, null);

        /// <summary>Creates a denied compose result.</summary>
        internal static AnonymousComposeResult Deny(DenyReason reason) => new(null, reason);
    }
}
