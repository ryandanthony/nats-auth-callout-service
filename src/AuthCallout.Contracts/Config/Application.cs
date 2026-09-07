// -----------------------------------------------------------------------
// <copyright file="Application.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Internal;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// An independent authorization domain: an issuer, a NATS target account, an ordered list
/// of permission profiles, an optional anonymous contribution, and optional event subjects
/// (<c>docs/design/02-authorization-model.md</c> §Application, ADR 0007). Two applications
/// are fully isolated: a token valid for one can never yield another's subjects.
/// </summary>
/// <param name="Name">Unique across the aggregate (validated).</param>
/// <param name="TargetAccount">The NATS account minted users for this application are placed in.</param>
/// <param name="Issuer">The OIDC authority and validation parameters for this application's tokens.</param>
/// <param name="Match">Optional app-level selector among same-issuer candidates (ADR 0003). Null when the issuer alone already identifies a single application.</param>
/// <param name="AnonymousProfile">This application's optional contribution to the anonymous union grant (ADR 0004). Null contributes nothing (opt-out).</param>
/// <param name="Profiles">Ordered; first match wins (ADR 0001).</param>
/// <param name="IdentityPropagation">Per-application identity-propagation toggle (ADR 0012). Null is treated as <see cref="IdentityPropagationConfig.Default"/> (enabled).</param>
/// <param name="Events">Auth outcome event subjects for this application (ADR 0008). Null disables event publishing for the application.</param>
public sealed record Application(
    string Name,
    string TargetAccount,
    IssuerConfig Issuer,
    ClaimMatch? Match,
    Grant? AnonymousProfile,
    IReadOnlyList<Profile> Profiles,
    IdentityPropagationConfig? IdentityPropagation,
    EventSubjects? Events)
{
    /// <summary>Gets the effective identity-propagation setting, defaulting to enabled when unset.</summary>
    /// <value>The effective identity-propagation setting.</value>
    public IdentityPropagationConfig EffectiveIdentityPropagation => IdentityPropagation ?? IdentityPropagationConfig.Default;

    /// <summary>Gets the application's stable slug, per ADR 0013's slugification rule.</summary>
    /// <value>The application's stable slug.</value>
    public AppSlug Slug => AppSlug.From(Name);

    /// <inheritdoc />
    public bool Equals(Application? other)
    {
        if (other is null)
        {
            return false;
        }

        var scalarsEqual = string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(TargetAccount, other.TargetAccount, StringComparison.Ordinal)
            && Issuer.Equals(other.Issuer);
        var optionalsEqual = Equals(Match, other.Match)
            && Equals(AnonymousProfile, other.AnonymousProfile)
            && Equals(IdentityPropagation, other.IdentityPropagation)
            && Equals(Events, other.Events);
        return scalarsEqual && optionalsEqual && StructuralEquality.SequenceEqual(Profiles, other.Profiles);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(TargetAccount, StringComparer.Ordinal);
        hash.Add(Issuer);
        hash.Add(Match);
        hash.Add(AnonymousProfile);
        hash.Add(IdentityPropagation);
        hash.Add(Events);
        hash.Add(StructuralEquality.SequenceHashCode(Profiles));
        return hash.ToHashCode();
    }
}
