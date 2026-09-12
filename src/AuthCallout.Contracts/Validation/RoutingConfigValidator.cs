// -----------------------------------------------------------------------
// <copyright file="RoutingConfigValidator.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.RegularExpressions;
using AuthCallout.Contracts.Config;

namespace AuthCallout.Contracts.Validation;

/// <summary>
/// The routing-aggregate half of <see cref="ConfigValidator"/> (doc 04 §Validation):
/// application/profile/template rules that need only the <see cref="RoutingConfig"/>
/// layer. Split out of <see cref="ConfigValidator"/> to keep each validator focused on one
/// config layer's type surface.
/// </summary>
internal static class RoutingConfigValidator
{
    private static readonly Regex TemplatePlaceholder = new(
        @"\{(?<name>[A-Za-z0-9_]+)\}",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>Validates a <see cref="RoutingConfig"/> aggregate. See <see cref="ConfigValidator.ValidateRouting"/>.</summary>
    internal static void Validate(RoutingConfig routing, List<ValidationError> errors)
    {
        ValidateUniqueApplicationNames(routing, errors);
        ValidateUniqueApplicationSlugs(routing, errors);
        ValidateUnambiguousIssuerMatch(routing, errors);

        foreach (var application in routing.Applications)
        {
            ValidateApplication(application, errors);
        }
    }

    private static void ValidateUniqueApplicationNames(RoutingConfig routing, List<ValidationError> errors)
    {
        var duplicates = routing.Applications
            .GroupBy(static a => a.Name, StringComparer.Ordinal)
            .Where(static g => g.Skip(1).Any())
            .Select(static g => g.Key);

        foreach (var name in duplicates)
        {
            errors.Add(new ValidationError(
                "duplicate-application-name",
                $"Application name '{name}' is used by more than one application; names must be unique across the aggregate."));
        }
    }

    private static void ValidateUniqueApplicationSlugs(RoutingConfig routing, List<ValidationError> errors)
    {
        var slugGroups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
#pragma warning disable S3267 // Accumulates into a shared dictionary and reports per-application errors; not a pure projection a Select() could express.
        foreach (var application in routing.Applications)
#pragma warning restore S3267
        {
            if (!AppSlug.TryFrom(application.Name, out var slug, out var error))
            {
                errors.Add(new ValidationError("invalid-application-slug", $"Application '{application.Name}': {error}"));
                continue;
            }

            if (!slugGroups.TryGetValue(slug.Value, out var names))
            {
                names = [];
                slugGroups[slug.Value] = names;
            }

            names.Add(application.Name);
        }

        foreach (var (slug, names) in slugGroups)
        {
            if (names.Count > 1)
            {
                errors.Add(new ValidationError(
                    "duplicate-application-slug",
                    $"Applications {string.Join(", ", names.Select(static n => $"'{n}'"))} all slugify to '{slug}' (ADR 0013); slugs must be collision-free."));
            }
        }
    }

    private static void ValidateUnambiguousIssuerMatch(RoutingConfig routing, List<ValidationError> errors)
    {
        foreach (var group in routing.Applications.GroupBy(static a => a.Issuer.Authority, StringComparer.Ordinal))
        {
            var candidates = group.ToArray();
            if (candidates.Length <= 1)
            {
                continue;
            }

            foreach (var application in candidates.Where(static a => a.Match is null))
            {
                errors.Add(new ValidationError(
                    "ambiguous-null-match",
                    $"Application '{application.Name}' has no 'match' but shares issuer '{group.Key}' with {candidates.Length - 1} other application(s); an empty/absent match is only valid when the issuer alone identifies a single application."));
            }

            ValidateNoIndistinguishablePairs(candidates, group.Key, errors);
            ValidateConsistentIssuerConfig(candidates, group.Key, errors);
        }
    }

    /// <summary>
    /// Applications sharing an issuer authority must carry <b>identical</b> issuer blocks
    /// (ADR 0016): stage 2 validates a token once per request against the authority's shared
    /// parameters (the resolution pipeline takes the first candidate's <see cref="IssuerConfig"/>),
    /// so any divergence — <c>validateAudience</c>/<c>audience</c>, <c>clockSkew</c>,
    /// <c>allowedAlgorithms</c>, <c>jwksUri</c>, cache/refresh settings — would make effective
    /// validation strictness depend on candidate ordering: a token failing one app's audience
    /// check could validate under a sibling's laxer parameters and then route to the strict
    /// app at stage 3. Divergence is therefore a config-load error (fail closed), leaning on
    /// <see cref="IssuerConfig"/>'s structural equality so every field is always covered.
    /// </summary>
    private static void ValidateConsistentIssuerConfig(Application[] candidates, string issuerAuthority, List<ValidationError> errors)
    {
        var reference = candidates[0];
        foreach (var application in candidates.Skip(1).Where(a => !a.Issuer.Equals(reference.Issuer)))
        {
            var message =
                $"Applications '{reference.Name}' and '{application.Name}' share issuer '{issuerAuthority}' but their issuer blocks differ; "
                + "all applications on one authority must use identical issuer settings (validation runs once per token against the shared "
                + "authority's parameters, ADR 0016), so a divergent block would make validation strictness depend on candidate ordering.";
            errors.Add(new ValidationError("divergent-issuer-config", message));
        }
    }

    private static void ValidateNoIndistinguishablePairs(Application[] candidates, string issuerAuthority, List<ValidationError> errors)
    {
        for (var i = 0; i < candidates.Length; i++)
        {
            for (var j = i + 1; j < candidates.Length; j++)
            {
                var left = candidates[i];
                var right = candidates[j];
                var bothMatchesAreEqual = left.Match is not null && right.Match is not null && left.Match.Equals(right.Match);
                if (!bothMatchesAreEqual)
                {
                    continue;
                }

                errors.Add(new ValidationError(
                    "indistinguishable-applications",
                    $"Applications '{left.Name}' and '{right.Name}' share issuer '{issuerAuthority}' and an identical 'match' predicate; they cannot be distinguished at resolution time."));
            }
        }
    }

    private static void ValidateApplication(Application application, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(application.Name))
        {
            errors.Add(new ValidationError("application-name-required", "An application's 'name' must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(application.TargetAccount))
        {
            errors.Add(new ValidationError("target-account-required", $"Application '{application.Name}': 'targetAccount' must not be empty."));
        }

        ValidateIssuer(application, errors);

        var propagationEnabled = application.EffectiveIdentityPropagation.Enabled;
        foreach (var profile in application.Profiles)
        {
            ValidateProfile(application, profile, propagationEnabled, errors);
        }
    }

    private static void ValidateIssuer(Application application, List<ValidationError> errors)
    {
        var issuer = application.Issuer;
        var prefix = $"Application '{application.Name}' issuer";

        if (string.IsNullOrWhiteSpace(issuer.Authority))
        {
            errors.Add(new ValidationError("issuer-authority-required", $"{prefix}: 'authority' must not be empty."));
        }

        if (issuer.ClockSkew.Value < TimeSpan.Zero)
        {
            errors.Add(new ValidationError("clock-skew-negative", $"{prefix}: 'clockSkew' ({issuer.ClockSkew}) must not be negative."));
        }
        else if (issuer.ClockSkew.Value > IssuerConfig.ClockSkewCap.Value)
        {
            errors.Add(new ValidationError(
                "clock-skew-exceeds-cap",
                $"{prefix}: 'clockSkew' ({issuer.ClockSkew}) exceeds the {IssuerConfig.ClockSkewCap} cap (ADR 0016)."));
        }
        else
        {
            // Within [0, cap]: no clock-skew violation to report.
        }

        foreach (var algorithm in issuer.AllowedAlgorithms.Where(a => !IssuerConfig.DefaultAllowedAlgorithms.Contains(a, StringComparer.Ordinal)))
        {
            errors.Add(new ValidationError(
                "algorithm-not-allowed",
                $"{prefix}: 'allowedAlgorithms' lists '{algorithm}', which is not an allowed asymmetric algorithm (HS*/none are never permitted, ADR 0016)."));
        }

        if (issuer.AllowedAlgorithms.Count == 0)
        {
            errors.Add(new ValidationError("algorithms-required", $"{prefix}: 'allowedAlgorithms' must list at least one algorithm."));
        }

        if (issuer.ValidateAudience && (issuer.Audience is null || issuer.Audience.Count == 0))
        {
            errors.Add(new ValidationError(
                "audience-required-when-validating",
                $"{prefix}: 'validateAudience' is true, so 'audience' must list at least one accepted audience (ADR 0016)."));
        }
    }

    private static void ValidateProfile(Application application, Profile profile, bool propagationEnabled, List<ValidationError> errors)
    {
        var prefix = $"Application '{application.Name}' profile '{profile.Name}'";

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add(new ValidationError("profile-name-required", $"Application '{application.Name}' has a profile with an empty 'name'."));
        }

        if (profile.AutoAddSessionAccess && !propagationEnabled)
        {
            errors.Add(new ValidationError(
                "auto-session-access-requires-propagation",
                $"{prefix}: 'autoAddSessionAccess' is true, but the application's identity propagation is disabled (ADR 0012); there is no token bucket to read."));
        }

        var declaredVariables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in profile.Variables)
        {
            ValidateVariableDeclaration(prefix, variable, declaredVariables, errors);
            ValidateTransforms(prefix, variable, errors);
        }

        ValidateTemplatePlaceholders(prefix, "publishTemplates", profile.PublishTemplates, declaredVariables, errors);
        ValidateTemplatePlaceholders(prefix, "subscribeTemplates", profile.SubscribeTemplates, declaredVariables, errors);
    }

    private static void ValidateVariableDeclaration(
        string prefix, TemplateVariable variable, HashSet<string> declaredVariables, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(variable.Name))
        {
            errors.Add(new ValidationError("variable-name-required", $"{prefix}: a template variable has an empty 'name'."));
        }
        else if (!declaredVariables.Add(variable.Name))
        {
            errors.Add(new ValidationError("duplicate-variable-name", $"{prefix}: template variable '{variable.Name}' is declared more than once."));
        }
        else
        {
            // First declaration of a non-empty variable name: nothing to report.
        }

        if (string.IsNullOrWhiteSpace(variable.Claim))
        {
            errors.Add(new ValidationError("variable-claim-required", $"{prefix}: template variable '{variable.Name}' has an empty 'claim' path."));
        }
    }

    private static void ValidateTransforms(string prefix, TemplateVariable variable, List<ValidationError> errors)
    {
        foreach (var transform in variable.Transforms)
        {
            switch (transform.Kind)
            {
                case TransformKind.Replace when string.IsNullOrEmpty(transform.From) || transform.To is null:
                    {
                        errors.Add(new ValidationError(
                            "malformed-replace-transform",
                            $"{prefix}: variable '{variable.Name}' has a 'replace' transform with a missing/empty 'from' or a missing 'to'."));
                        break;
                    }

                case TransformKind.Prefix or TransformKind.Suffix when transform.Text is null:
                    {
                        errors.Add(new ValidationError(
                            "malformed-affix-transform",
                            $"{prefix}: variable '{variable.Name}' has a '{(transform.Kind == TransformKind.Prefix ? "prefix" : "suffix")}' transform with a missing argument."));
                        break;
                    }

                default:
                    {
                        // Lowercase/Uppercase/Sanitize/Hash/RequireSafe, or a well-formed Replace/Prefix/Suffix: no shape error.
                        break;
                    }
            }
        }
    }

    private static void ValidateTemplatePlaceholders(
        string prefix, string fieldName, IReadOnlyList<string> templates, HashSet<string> declaredVariables, List<ValidationError> errors)
    {
        foreach (var template in templates)
        {
            foreach (Match match in TemplatePlaceholder.Matches(template))
            {
                var name = match.Groups["name"].Value;
                if (!declaredVariables.Contains(name))
                {
                    errors.Add(new ValidationError(
                        "unknown-template-variable",
                        $"{prefix}: '{fieldName}' entry '{template}' references undeclared variable '{{{name}}}'."));
                }
            }
        }
    }
}
