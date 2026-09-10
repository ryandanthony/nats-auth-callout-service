// -----------------------------------------------------------------------
// <copyright file="ResolutionPipeline.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Core.Pipeline;

/// <summary>
/// The fixed issuer → validate → app → profile → grant resolution pipeline (doc 03, ADR
/// 0003). Stateless and thread-safe: holds no mutable state of its own beyond its immutable
/// <see cref="ITokenValidator"/> dependency, and every call resolves purely from its
/// parameters plus the passed-in <see cref="ActiveConfig"/> snapshot (doc 08 §In-process
/// parallelism prerequisite), so many concurrent calls (<c>Parallel.ForEachAsync</c>, doc 08)
/// over the same or different snapshots never interfere.
/// </summary>
public sealed class ResolutionPipeline : IResolutionPipeline
{
    private readonly ITokenValidator _tokenValidator;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="ResolutionPipeline"/> class.</summary>
    /// <param name="tokenValidator">Stage 2's token validator (ADR 0016), consumed via the <see cref="ITokenValidator"/> interface only — Core stays transport/identity-free.</param>
    /// <param name="timeProvider">Time source for the anonymous grant's expiry computation (test seam; defaults to <see cref="TimeProvider.System"/>).</param>
    public ResolutionPipeline(ITokenValidator tokenValidator, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(tokenValidator);
        _tokenValidator = tokenValidator;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<Decision> ResolveAsync(AuthRequest request, ActiveConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(config);

        return request.IsAnonymous
            ? Task.FromResult(ResolveAnonymous(config))
            : ResolveTokenAsync(request, config, cancellationToken);
    }

    private Decision ResolveAnonymous(ActiveConfig config)
    {
        var result = AnonymousComposer.Compose(config.Routing, config.Bootstrap.Anonymous, _timeProvider.GetUtcNow());
        return result.Decision is { } decision
            ? decision
            : new DeniedDecision(Application: null, Principal: null, Issuer: null, result.DenyReason!.Value);
    }

    private async Task<Decision> ResolveTokenAsync(AuthRequest request, ActiveConfig config, CancellationToken cancellationToken)
    {
        var rawToken = request.RawToken!;

        // Stage 1 (doc 03): read iss unvalidated to find candidate applications and the
        // shared JWKS authority; no other claim is trusted before Stage 2 succeeds (ADR 0003
        // §Trust boundary).
        var unvalidatedIssuer = UnvalidatedIssuerReader.TryReadIssuer(rawToken);
        if (unvalidatedIssuer is null || !config.Routing.IssuerIndex.TryGetValue(unvalidatedIssuer, out var candidates))
        {
            return Deny(null, null, unvalidatedIssuer, DenyReason.UnknownIssuer);
        }

        // Stage 2 (doc 03, ADR 0016): validate once against the shared issuer JWKS. All
        // candidates share one issuer.Authority (RoutingConfig.IssuerIndex is keyed by it),
        // so any candidate's IssuerConfig carries the same validation parameters; the first
        // is used to select them.
        var issuerConfig = candidates[0].Issuer;
        var validation = await _tokenValidator.ValidateAsync(rawToken, issuerConfig, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return Deny(null, null, unvalidatedIssuer, validation.Reason!.Value);
        }

        var claims = validation.Claims!;
        var principal = claims.GetSingleValue("sub") ?? claims.GetSingleValue("azp");

        // Stage 3 (doc 03, ADR 0003): application Match among the issuer's candidates.
        var appSelection = AppSelector.Select(candidates, claims);
        if (appSelection.DenyReason is { } appDenyReason)
        {
            return Deny(null, principal, unvalidatedIssuer, appDenyReason);
        }

        var application = appSelection.Application!;

        // Stage 4 (doc 03, ADR 0001): first matching profile, in declared order.
        var profile = ProfileSelector.SelectFirstMatch(application.Profiles, claims);
        if (profile is null)
        {
            return Deny(application.Name, principal, unvalidatedIssuer, DenyReason.NoProfileMatch);
        }

        // Grant construction (doc 03 §Grant construction, ADR 0009, D19): expiry from the
        // token's own exp, guaranteed present by ADR 0016 (RequireExpirationTime).
        var expiry = ResolveExpiry(claims);
        var buildResult = GrantBuilder.Build(application, profile, claims, principal, unvalidatedIssuer, expiry);
        return buildResult.Decision is { } granted
            ? granted
            : Deny(application.Name, principal, unvalidatedIssuer, buildResult.DenyReason!.Value);
    }

    /// <summary>
    /// Resolves the minted JWT's absolute expiry from the token's own validated <c>exp</c>
    /// claim (doc 03 §Grant construction: "always present: validation requires an expiration
    /// time, ADR 0016"). <c>exp</c> is a JSON-numeric claim, so <see cref="IClaimsAccessor.GetSingleValue"/>
    /// yields its raw invariant-culture numeric text (<see cref="AuthCallout.Core.Claims.ClaimsAccessor"/>'s
    /// pinned coercion rule) — Unix seconds since epoch, per the JWT spec (RFC 7519 §4.1.4).
    /// </summary>
    private static DateTimeOffset ResolveExpiry(IClaimsAccessor claims)
    {
        var rawExp = claims.GetSingleValue("exp");

        // ADR 0016's RequireExpirationTime guarantees exp is present and lifetime-validated
        // by the time this pipeline stage runs; a missing/malformed value here would mean
        // ITokenValidator returned a claim set that violates its own contract.
        return DateTimeOffset.FromUnixTimeSeconds(long.Parse(rawExp!, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static DeniedDecision Deny(string? application, string? principal, string? issuer, DenyReason reason) =>
        new(application, principal, issuer, reason);
}
