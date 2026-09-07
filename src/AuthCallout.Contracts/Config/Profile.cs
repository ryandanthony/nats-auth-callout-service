// -----------------------------------------------------------------------
// <copyright file="Profile.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Internal;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// A named permission profile inside an application (ADR 0001, ADR 0007). A connection's
/// permissions come from the first profile in an <see cref="Application"/>'s
/// <see cref="Application.Profiles"/> whose <see cref="Match"/> is satisfied.
/// </summary>
/// <param name="Name">Identifier for logging/metrics.</param>
/// <param name="Match">Required claim-based predicate selecting this profile post-validation.</param>
/// <param name="Grant">Static publish/subscribe subjects.</param>
/// <param name="NameClaim">The claim used as the minted user JWT's <c>name</c>. Falls back to <c>sub</c> when absent (D19); <c>sub</c> also absent is <see cref="Decisions.DenyReason.NameClaimError"/>.</param>
/// <param name="Variables">Named template variables bound to (optionally transformed) claims (ADR 0009).</param>
/// <param name="PublishTemplates">Subject patterns with <c>{name}</c> placeholders resolved from <see cref="Variables"/>, expanded and added to the publish grant.</param>
/// <param name="SubscribeTemplates">Subject patterns with <c>{name}</c> placeholders resolved from <see cref="Variables"/>, expanded and added to the subscribe grant.</param>
/// <param name="AutoAddSessionAccess">
/// When true, the minted grant also gets read access to this application's token KV bucket
/// (<c>callout_apps_{app-slug}_oidc_tokens.&gt;</c>, ADR 0010). Default false. A config-load
/// error when the owning application's identity propagation is disabled (ADR 0012, nothing
/// to read).
/// </param>
#pragma warning disable CA1724 // "Profile" matches doc 04/07/09's canonical model name exactly; System.Web.Profile is not referenced anywhere in this service (no ASP.NET Framework dependency).
public sealed record Profile(
    string Name,
    ClaimMatch Match,
    Grant Grant,
    string NameClaim,
    IReadOnlyList<TemplateVariable> Variables,
    IReadOnlyList<string> PublishTemplates,
    IReadOnlyList<string> SubscribeTemplates,
    bool AutoAddSessionAccess)
{
    /// <inheritdoc />
    public bool Equals(Profile? other)
    {
        if (other is null)
        {
            return false;
        }

        var identityEqual = string.Equals(Name, other.Name, StringComparison.Ordinal)
            && Match.Equals(other.Match)
            && Grant.Equals(other.Grant);
        var behaviorEqual = string.Equals(NameClaim, other.NameClaim, StringComparison.Ordinal)
            && AutoAddSessionAccess == other.AutoAddSessionAccess;
        var templatesEqual = StructuralEquality.SequenceEqual(Variables, other.Variables)
            && StructuralEquality.SequenceEqual(PublishTemplates, other.PublishTemplates)
            && StructuralEquality.SequenceEqual(SubscribeTemplates, other.SubscribeTemplates);
        return identityEqual && behaviorEqual && templatesEqual;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Name);
        hash.Add(Match);
        hash.Add(Grant);
        hash.Add(NameClaim);
        hash.Add(AutoAddSessionAccess);
        hash.Add(StructuralEquality.SequenceHashCode(Variables));
        hash.Add(StructuralEquality.SequenceHashCode(PublishTemplates));
        hash.Add(StructuralEquality.SequenceHashCode(SubscribeTemplates));
        return hash.ToHashCode();
    }
}
#pragma warning restore CA1724
