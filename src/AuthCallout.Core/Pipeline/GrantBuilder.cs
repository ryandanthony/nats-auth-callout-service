// -----------------------------------------------------------------------
// <copyright file="GrantBuilder.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;
using AuthCallout.Core.Templates;

namespace AuthCallout.Core.Pipeline;

/// <summary>
/// Grant construction for a matched profile (doc 03 §Grant construction): static subjects +
/// template-expanded subjects (P04's <see cref="VariableResolver"/>/<see cref="TemplateExpander"/>),
/// the minted <c>name</c> (P04's <see cref="NameResolver"/>), and the auto-added KV-read
/// subjects (doc 03/07, ADR 0010/0011/0012/0013). Pure: no I/O, no mutable state.
/// </summary>
internal static class GrantBuilder
{
    /// <summary>
    /// Builds a <see cref="GrantedDecision"/> for <paramref name="profile"/> matched within
    /// <paramref name="application"/>.
    /// </summary>
    /// <param name="application">The selected application (supplies target account, slug, and identity-propagation toggle).</param>
    /// <param name="profile">The matched profile.</param>
    /// <param name="claims">The token's validated claims.</param>
    /// <param name="principal">The <c>sub</c>/<c>azp</c>-style principal identifier for the decision (ADR 0008).</param>
    /// <param name="issuer">The token's issuer authority.</param>
    /// <param name="expiry">The minted JWT's expiry: the token's own <c>exp</c> (doc 03 §Grant construction, ADR 0016 guarantees presence).</param>
    /// <returns>The built <see cref="GrantedDecision"/>, or the specific <see cref="DenyReason"/> when grant construction fails (a required variable missing, an unsafe transformed value, or neither <c>nameClaim</c> nor <c>sub</c> resolving).</returns>
    internal static GrantBuildResult Build(
        Application application,
        Profile profile,
        IClaimsAccessor claims,
        string? principal,
        string? issuer,
        DateTimeOffset expiry)
    {
        var nameDenial = NameResolver.TryResolve(profile.NameClaim, claims, out var name);
        if (nameDenial is { } nameError)
        {
            return GrantBuildResult.Deny(nameError);
        }

        var variableDenial = VariableResolver.TryResolveAll(profile.Variables, claims, out var resolvedVariables);
        if (variableDenial is { } variableError)
        {
            return GrantBuildResult.Deny(variableError);
        }

        var subscribe = BuildSubscribe(application, profile, resolvedVariables, claims, inboxPrincipal: principal ?? name!, out var selfLookupKey, out var readBuckets);
        var publish = BuildPublish(profile, resolvedVariables, readBuckets);

        var decision = new GrantedDecision(
            Application: application.Name,
            Principal: principal,
            Issuer: issuer,
            TargetAccount: application.TargetAccount,
            Name: name!,
            Expiry: expiry,
            Publish: publish,
            Subscribe: subscribe,
            ProfileName: profile.Name,
            IdentityPropagationEnabled: application.EffectiveIdentityPropagation.Enabled,
            SelfLookupKey: selfLookupKey);
        return GrantBuildResult.Success(decision);
    }

#pragma warning disable S4017 // IReadOnlyDictionary<string, IReadOnlyList<string>> is VariableResolver/TemplateExpander's actual shared output shape (see their own S4017 suppressions), not incidental nesting.
    private static IReadOnlyList<string> BuildPublish(
        Profile profile, IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedVariables, IReadOnlyList<string> readBuckets)
#pragma warning restore S4017
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var publish = new List<string>();
        SubjectListBuilder.AddRange(publish, seen, profile.Grant.Publish);
        SubjectListBuilder.AddRange(publish, seen, TemplateExpander.ExpandAll(profile.PublishTemplates, resolvedVariables));

        // For every KV bucket this grant can read (via the $KV.* subscribe subjects added in
        // BuildSubscribe), a real JetStream KV client (NatsKVContext) also has to publish
        // stream-info / direct-get requests to that bucket's backing stream. Scoped per backing
        // stream (not $JS.API.>), so the grant reaches only the buckets it was granted (Ryan's
        // option A: the callout auto-adds the wire-level JS-API plumbing ADR 0013's logical
        // $KV grant implies). The _INBOX reply path is added on the subscribe side.
        foreach (var bucket in readBuckets)
        {
            SubjectListBuilder.AddRange(publish, seen, AppSlug.JetStreamReadApiPublishSubjects(bucket));
        }

        return publish;
    }

#pragma warning disable S4017 // IReadOnlyDictionary<string, IReadOnlyList<string>> is VariableResolver/TemplateExpander's actual shared output shape (see their own S4017 suppressions), not incidental nesting.
    private static IReadOnlyList<string> BuildSubscribe(
        Application application,
        Profile profile,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedVariables,
        IClaimsAccessor claims,
        string inboxPrincipal,
        out string? selfLookupKey,
        out IReadOnlyList<string> readBuckets)
#pragma warning restore S4017
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var subscribe = new List<string>();
        SubjectListBuilder.AddRange(subscribe, seen, profile.Grant.Subscribe);
        SubjectListBuilder.AddRange(subscribe, seen, TemplateExpander.ExpandAll(profile.SubscribeTemplates, resolvedVariables));

        var slug = application.Slug;
        var buckets = new List<string>();

        // doc 03 §Grant construction: autoAddSessionAccess -> token-bucket read.
        if (profile.AutoAddSessionAccess)
        {
            SubjectListBuilder.AddOne(subscribe, seen, slug.OidcTokensReadSubject);
            buckets.Add(slug.OidcTokensBucket);
        }

        // doc 03/07: every authenticated (non-anonymous) grant gets keys + discovery reads,
        // unconditionally, regardless of the identity-propagation toggle (ADR 0011/0012).
        SubjectListBuilder.AddOne(subscribe, seen, AppSlug.AssertionKeysReadSubject);
        buckets.Add(AppSlug.AssertionKeysBucket);
        SubjectListBuilder.AddOne(subscribe, seen, slug.DiscoveryReadSubject);
        buckets.Add(slug.DiscoveryBucket);

        selfLookupKey = ResolveSelfLookupKey(application, claims);
        if (selfLookupKey is { } key)
        {
            SubjectListBuilder.AddOne(subscribe, seen, slug.AssertionSelfLookupSubject(key));
            buckets.Add(slug.AssertionsBucket);
        }

        // Reply path for the JetStream KV read requests added on the publish side: the KV
        // client receives stream-info / direct-get replies on its connection inbox. Scoped to
        // this connection's own principal-derived prefix (never the account-wide _INBOX.>,
        // which would let any authenticated client read every other client's replies); the
        // client must set the matching custom inbox prefix (AppSlug.ClientInboxPrefix).
        // Added once because the grant reads at least one KV bucket (keys + discovery are
        // unconditional). The principal falls back to the minted name, which NameResolver
        // guarantees non-null by this point (see the Build call site).
        SubjectListBuilder.AddOne(subscribe, seen, slug.ClientInboxSubscribeSubject(inboxPrincipal));

        readBuckets = buckets;
        return subscribe;
    }

    /// <summary>
    /// Resolves the client's own assertion self-lookup key: <c>jti</c>, falling back to
    /// <c>sub</c> (doc 07 §Key parts), and only when the application's identity propagation
    /// is enabled (doc 03 §Grant construction: "own-assertion read... when propagation
    /// enabled and jti (or sub fallback) present"). Neither present, or propagation
    /// disabled, yields null: propagation becomes unavailable for the connection but the
    /// grant still stands (doc 07 §Key parts).
    /// </summary>
    private static string? ResolveSelfLookupKey(Application application, IClaimsAccessor claims)
    {
        if (!application.EffectiveIdentityPropagation.Enabled)
        {
            return null;
        }

        return claims.GetSingleValue("jti") ?? claims.GetSingleValue("sub");
    }

    /// <summary>The outcome of <see cref="Build"/>: either the constructed grant, or the specific denial.</summary>
    internal readonly record struct GrantBuildResult
    {
        private GrantBuildResult(GrantedDecision? decision, DenyReason? denyReason)
        {
            Decision = decision;
            DenyReason = denyReason;
        }

        /// <summary>Gets the constructed grant, present only when <see cref="DenyReason"/> is null.</summary>
        /// <value>The constructed grant, or null on denial.</value>
        internal GrantedDecision? Decision { get; }

        /// <summary>Gets the denial reason, present only when <see cref="Decision"/> is null.</summary>
        /// <value>The denial reason, or null on success.</value>
        internal DenyReason? DenyReason { get; }

        /// <summary>Creates a successful build result.</summary>
        internal static GrantBuildResult Success(GrantedDecision decision) => new(decision, null);

        /// <summary>Creates a denied build result.</summary>
        internal static GrantBuildResult Deny(DenyReason reason) => new(null, reason);
    }
}
