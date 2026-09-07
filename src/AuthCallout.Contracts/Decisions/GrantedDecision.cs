// -----------------------------------------------------------------------
// <copyright file="GrantedDecision.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Internal;

namespace AuthCallout.Contracts.Decisions;

/// <summary>
/// A successful resolution: a grant was constructed, either from a matched profile or the
/// anonymous union (doc 03 §Grant construction, ADR 0004). Carries the fully-expanded
/// publish/subscribe subject lists (static + template-expanded + auto-added KV reads) and
/// every field the minter, identity-store writer, and event emitter need.
/// </summary>
/// <param name="Application">The selected application's name, or null for an anonymous grant (ADR 0004: the union is not tied to one application).</param>
/// <param name="Principal">The <c>sub</c>/<c>azp</c>-style principal identifier, or null for an anonymous grant (no token).</param>
/// <param name="Issuer">The token's original issuer, or null for an anonymous grant.</param>
/// <param name="TargetAccount">The NATS account the minted user JWT is placed in (the application's account, or the global anonymous account).</param>
/// <param name="Name">The minted JWT's <c>name</c>: the profile's <c>nameClaim</c> value (falling back to <c>sub</c>, D19), or a fixed anonymous label for the anonymous case.</param>
/// <param name="Expiry">The minted JWT's expiry: the token's own <c>exp</c> (ADR 0016 guarantees presence) for a profile grant, or the global <see cref="Config.AnonymousGlobalConfig.Expiry"/> for anonymous.</param>
/// <param name="Publish">The fully-resolved publish subject list: static grant subjects + expanded publish templates. Auto-added KV-read subjects are never publish subjects.</param>
/// <param name="Subscribe">The fully-resolved subscribe subject list: static grant subjects + expanded subscribe templates + any auto-added KV-read subjects (session access, keys, discovery, self-lookup).</param>
/// <param name="ProfileName">The matched profile's name, or null for an anonymous grant (there is no profile).</param>
/// <param name="IdentityPropagationEnabled">Whether the selected application has identity propagation enabled (ADR 0012). Always false for an anonymous grant (no application, no propagation work, doc 03).</param>
/// <param name="SelfLookupKey">
/// The client's own <c>jti</c> (or <c>sub</c> fallback, doc 07) to use as the assertion
/// self-lookup key, when propagation is enabled and one is available; null when
/// propagation is disabled, the grant is anonymous, or neither <c>jti</c> nor <c>sub</c>
/// was present (identity propagation becomes unavailable for the connection, but the
/// grant still stands, doc 07).
/// </param>
public sealed record GrantedDecision(
    string? Application,
    string? Principal,
    string? Issuer,
    string TargetAccount,
    string Name,
    DateTimeOffset Expiry,
    IReadOnlyList<string> Publish,
    IReadOnlyList<string> Subscribe,
    string? ProfileName,
    bool IdentityPropagationEnabled,
    string? SelfLookupKey)
    : Decision(Application, Principal, Issuer)
{
    /// <inheritdoc />
    public bool Equals(GrantedDecision? other)
    {
        if (other is null)
        {
            return false;
        }

        var identityEqual = string.Equals(Application, other.Application, StringComparison.Ordinal)
            && string.Equals(Principal, other.Principal, StringComparison.Ordinal)
            && string.Equals(Issuer, other.Issuer, StringComparison.Ordinal);
        var grantEqual = string.Equals(TargetAccount, other.TargetAccount, StringComparison.Ordinal)
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && Expiry == other.Expiry;
        var propagationEqual = string.Equals(ProfileName, other.ProfileName, StringComparison.Ordinal)
            && IdentityPropagationEnabled == other.IdentityPropagationEnabled
            && string.Equals(SelfLookupKey, other.SelfLookupKey, StringComparison.Ordinal);
        var subjectsEqual = StructuralEquality.SequenceEqual(Publish, other.Publish)
            && StructuralEquality.SequenceEqual(Subscribe, other.Subscribe);
        return identityEqual && grantEqual && propagationEqual && subjectsEqual;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Application);
        hash.Add(Principal);
        hash.Add(Issuer);
        hash.Add(TargetAccount);
        hash.Add(Name);
        hash.Add(Expiry);
        hash.Add(ProfileName);
        hash.Add(IdentityPropagationEnabled);
        hash.Add(SelfLookupKey);
        hash.Add(StructuralEquality.SequenceHashCode(Publish));
        hash.Add(StructuralEquality.SequenceHashCode(Subscribe));
        return hash.ToHashCode();
    }
}
