// -----------------------------------------------------------------------
// <copyright file="ConfigValidator.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;

namespace AuthCallout.Contracts.Validation;

/// <summary>
/// The aggregate validator for the canonical config model, used by every provider
/// (files/k8s/postgres) and by the host's config-apply loop before a batch or the bootstrap
/// layer is ever accepted (doc 04 §Validation, ADR 0006 §Steady-state failure: reject the
/// whole batch, keep last-good). Every rule cites the doc/ADR it implements; error text is
/// deterministic (stable for the same input across runs) because it surfaces verbatim in
/// the k8s CRD's <c>status.reason</c> and in reject-batch logs. The per-layer rule bodies
/// live in <see cref="RoutingConfigValidator"/> and <see cref="BootstrapConfigValidator"/>;
/// this class is the public entry point and owns the cross-layer rule.
/// </summary>
public static class ConfigValidator
{
    /// <summary>
    /// Validates a <see cref="RoutingConfig"/> aggregate in isolation: uniqueness, ambiguity,
    /// per-application/profile rules, and template well-formedness (doc 04 §Validation).
    /// Does not require the bootstrap layer; see <see cref="ValidateActiveConfig"/> for the
    /// cross-layer checks (e.g. assertion-keyset presence when propagation is enabled
    /// anywhere).
    /// </summary>
    /// <param name="routing">The routing aggregate to validate.</param>
    /// <returns>The validation result: valid, or every violated rule.</returns>
    public static ValidationResult ValidateRouting(RoutingConfig routing)
    {
        ArgumentNullException.ThrowIfNull(routing);

        var errors = new List<ValidationError>();
        RoutingConfigValidator.Validate(routing, errors);
        return ValidationResult.Invalid(errors);
    }

    /// <summary>
    /// Validates a <see cref="BootstrapConfig"/> in isolation: seed shapes, the assertion
    /// keyset invariants (ADR 0014), source settings, JWKS/concurrency bounds, and the
    /// anonymous global config (doc 04 §Validation, ADR 0016 for validation-parameter caps
    /// that also apply at the issuer level — see <see cref="ValidateRouting"/>).
    /// </summary>
    /// <param name="bootstrap">The bootstrap config to validate.</param>
    /// <returns>The validation result: valid, or every violated rule.</returns>
    public static ValidationResult ValidateBootstrap(BootstrapConfig bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);

        var errors = new List<ValidationError>();
        BootstrapConfigValidator.Validate(bootstrap, errors);
        return ValidationResult.Invalid(errors);
    }

    /// <summary>
    /// Validates the cross-layer invariants that need both the bootstrap and routing
    /// layers together, in addition to running <see cref="ValidateBootstrap"/> and
    /// <see cref="ValidateRouting"/>: an assertion signing key is required (ADR 0011:
    /// "only needed when assertions are enabled") whenever at least one application has
    /// identity propagation enabled (ADR 0012).
    /// </summary>
    /// <param name="bootstrap">The bootstrap config.</param>
    /// <param name="routing">The routing aggregate.</param>
    /// <returns>The validation result: valid, or every violated rule across both layers plus the cross-layer check.</returns>
    public static ValidationResult ValidateActiveConfig(BootstrapConfig bootstrap, RoutingConfig routing)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(routing);

        var errors = new List<ValidationError>();
        BootstrapConfigValidator.Validate(bootstrap, errors);
        RoutingConfigValidator.Validate(routing, errors);

        var propagationEnabledAnywhere = routing.Applications.Any(static a => a.EffectiveIdentityPropagation.Enabled);
        var hasUsableKeyset = bootstrap.AssertionKeys is { Count: > 0 };
        if (propagationEnabledAnywhere && !hasUsableKeyset)
        {
            errors.Add(new ValidationError(
                "assertion-keyset-required",
                "At least one application has identity propagation enabled, so bootstrap 'assertionKeys' must declare at least one key (ADR 0011)."));
        }

        return ValidationResult.Invalid(errors);
    }
}
