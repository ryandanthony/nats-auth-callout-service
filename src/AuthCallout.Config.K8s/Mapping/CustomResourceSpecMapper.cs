// -----------------------------------------------------------------------
// <copyright file="CustomResourceSpecMapper.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Config.K8s.Mapping;

/// <summary>
/// Maps an <c>AuthApplication</c> Custom Resource's <c>spec</c> (doc 09 §4) into the canonical
/// <see cref="Application"/> model. The CRD's OpenAPI v3 validation schema enforces required
/// fields and types at admission time (doc 09 §4: "a malformed CR is rejected by the API server
/// before the service sees it"), so this mapper's own checks are a defensive second layer, not
/// the primary gate — but it still reports every problem it finds deterministically, the same
/// way <c>AuthCallout.Config.Files</c>' mapper does, so a CR that somehow bypasses/predates
/// schema enforcement (e.g. a stale stored object from before a schema tightening) still fails
/// closed with a clear reason rather than throwing. <c>spec</c> is the same canonical
/// <b>Application</b> shape as the file schema, minus the file's <c>schemaVersion</c>/<c>name</c>
/// (the CR's <c>metadata.name</c> supplies the application name, doc 09 §4).
/// </summary>
public static class CustomResourceSpecMapper
{
    private const string MatchFieldName = "match";

    private static readonly HashSet<string> SpecFields = new(StringComparer.Ordinal)
    {
        "targetAccount", "issuer", MatchFieldName,
        "identityPropagation", "anonymousProfile", "events", "profiles",
    };

    private static readonly HashSet<string> IssuerFields = new(StringComparer.Ordinal)
    {
        "authority", "jwksUri", "jwksCache", "refreshInterval",
        "validateAudience", "audience", "clockSkew", "allowedAlgorithms",
    };

    private static readonly HashSet<string> ProfileFields = new(StringComparer.Ordinal)
    {
        "name", MatchFieldName, "grant", "nameClaim", "autoAddSessionAccess",
        "variables", "publishTemplates", "subscribeTemplates",
    };

    private static readonly HashSet<string> GrantFields = new(StringComparer.Ordinal) { "publish", "subscribe" };

    private static readonly HashSet<string> EventFields = new(StringComparer.Ordinal) { "success", "failure" };

    private static readonly HashSet<string> IdentityPropagationFields = new(StringComparer.Ordinal) { "enabled" };

    private static readonly HashSet<string> TemplateVariableFields = new(StringComparer.Ordinal)
    {
        "name", "claim", "required", "transforms",
    };

    /// <summary>
    /// Attempts to map a Custom Resource's <c>metadata.name</c> and <c>spec</c> into the
    /// canonical model.
    /// </summary>
    /// <param name="applicationName">The application name (the CR's <c>metadata.name</c>).</param>
    /// <param name="spec">The CR's <c>spec</c> element (an object).</param>
    /// <returns>
    /// The mapping result: a populated <see cref="Application"/> when every rule passed, or a
    /// deterministic error describing every problem found (all violations collected, not
    /// fail-fast on the first, mirroring <c>ConfigValidator</c>'s and P10's convention).
    /// </returns>
    public static CustomResourceMapResult TryMap(string applicationName, JsonElement spec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        if (spec.ValueKind != JsonValueKind.Object)
        {
            return CustomResourceMapResult.Failure("Custom Resource 'spec' must be an object.");
        }

        var errors = new List<string>();
        ValidateNoUnknownProperties(spec, SpecFields, "spec", errors);

        var fields = MapSpecFields(spec, errors);

        if (errors.Count > 0 || fields.TargetAccount is null || fields.Issuer is null)
        {
            return CustomResourceMapResult.Failure(string.Join(" ", errors));
        }

        var application = new Application(
            Name: applicationName,
            TargetAccount: fields.TargetAccount,
            Issuer: fields.Issuer,
            Match: fields.Match,
            AnonymousProfile: fields.AnonymousProfile,
            Profiles: fields.Profiles,
            IdentityPropagation: fields.IdentityPropagation,
            Events: fields.Events);

        return CustomResourceMapResult.Success(application);
    }

    /// <summary>Intermediate, partially-populated spec fields collected before the final <see cref="Application"/> is constructed.</summary>
    private sealed record SpecFieldSet(
        string? TargetAccount,
        IssuerConfig? Issuer,
        ClaimMatch? Match,
        IdentityPropagationConfig? IdentityPropagation,
        Grant? AnonymousProfile,
        EventSubjects? Events,
        IReadOnlyList<Profile> Profiles);

    private static SpecFieldSet MapSpecFields(JsonElement spec, List<string> errors)
    {
        var targetAccount = RequireString(spec, "targetAccount", "spec.targetAccount", errors);

        IssuerConfig? issuer = null;
        if (!spec.TryGetProperty("issuer", out var issuerEl) || issuerEl.ValueKind != JsonValueKind.Object)
        {
            errors.Add("'spec.issuer' is required and must be an object.");
        }
        else
        {
            issuer = MapIssuer(issuerEl, errors);
        }

        ClaimMatch? match = spec.TryGetProperty(MatchFieldName, out var matchEl)
            ? MapClaimMatch(matchEl, "spec.match", errors)
            : null;

        IdentityPropagationConfig? identityPropagation = spec.TryGetProperty("identityPropagation", out var ipEl)
            ? MapIdentityPropagation(ipEl, errors)
            : null;

        Grant? anonymousProfile = spec.TryGetProperty("anonymousProfile", out var anonEl)
            ? MapGrant(anonEl, "spec.anonymousProfile", errors)
            : null;

        EventSubjects? events = spec.TryGetProperty("events", out var eventsEl)
            ? MapEventSubjects(eventsEl, errors)
            : null;

        var profiles = spec.TryGetProperty("profiles", out var profilesEl) && profilesEl.ValueKind == JsonValueKind.Array
            ? MapProfiles(profilesEl, errors)
            : [];

        return new SpecFieldSet(targetAccount, issuer, match, identityPropagation, anonymousProfile, events, profiles);
    }

    private static IssuerConfig? MapIssuer(JsonElement el, List<string> errors)
    {
        ValidateNoUnknownProperties(el, IssuerFields, "spec.issuer", errors);

        var authority = RequireString(el, "authority", "spec.issuer.authority", errors);
        var jwksUri = MapNullableString(el, "jwksUri", "spec.issuer.jwksUri", errors);
        var jwksCache = RequireDuration(el, "jwksCache", "spec.issuer.jwksCache", errors);
        var refreshInterval = MapNullableDuration(el, "refreshInterval", "spec.issuer.refreshInterval", errors);
        var validateAudience = MapBoolWithDefault(el, "validateAudience", defaultValue: true, "spec.issuer.validateAudience", errors);
        var audience = MapOptionalStringArray(el, "audience", "spec.issuer.audience", errors);
        var clockSkew = MapDurationWithDefault(el, "clockSkew", IssuerConfig.DefaultClockSkew, "spec.issuer.clockSkew", errors);
        var allowedAlgorithms = MapOptionalStringArray(el, "allowedAlgorithms", "spec.issuer.allowedAlgorithms", errors)
            ?? IssuerConfig.DefaultAllowedAlgorithms;

        if (authority is null || jwksCache is null)
        {
            return null;
        }

        return new IssuerConfig(
            authority,
            jwksUri,
            jwksCache.Value,
            refreshInterval,
            validateAudience,
            audience,
            clockSkew,
            allowedAlgorithms);
    }

    private static IdentityPropagationConfig? MapIdentityPropagation(JsonElement el, List<string> errors)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            errors.Add("'spec.identityPropagation' must be an object.");
            return null;
        }

        ValidateNoUnknownProperties(el, IdentityPropagationFields, "spec.identityPropagation", errors);

        if (!el.TryGetProperty("enabled", out var enabledEl)
            || (enabledEl.ValueKind != JsonValueKind.True && enabledEl.ValueKind != JsonValueKind.False))
        {
            errors.Add("'spec.identityPropagation.enabled' is required and must be a boolean.");
            return null;
        }

        return new IdentityPropagationConfig(enabledEl.GetBoolean());
    }

    private static Grant MapGrant(JsonElement el, string context, List<string> errors)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"'{context}' must be an object with 'publish'/'subscribe' arrays.");
            return Grant.Empty;
        }

        ValidateNoUnknownProperties(el, GrantFields, context, errors);

        var publish = el.TryGetProperty("publish", out var pubEl)
            ? MapStringArray(pubEl, $"{context}.publish", errors)
            : [];
        var subscribe = el.TryGetProperty("subscribe", out var subEl)
            ? MapStringArray(subEl, $"{context}.subscribe", errors)
            : [];

        return new Grant(publish, subscribe);
    }

    private static EventSubjects MapEventSubjects(JsonElement el, List<string> errors)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            errors.Add("'spec.events' must be an object.");
            return new EventSubjects(Success: null, Failure: null);
        }

        ValidateNoUnknownProperties(el, EventFields, "spec.events", errors);

        var success = MapNullableString(el, "success", "spec.events.success", errors);
        var failure = MapNullableString(el, "failure", "spec.events.failure", errors);
        return new EventSubjects(success, failure);
    }

    private static ClaimMatch MapClaimMatch(JsonElement el, string context, List<string> errors)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"'{context}' must be an object mapping claim paths to value arrays.");
            return ClaimMatch.Empty;
        }

        var predicates = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var prop in el.EnumerateObject())
        {
            predicates[prop.Name] = MapStringArray(prop.Value, $"{context}.{prop.Name}", errors);
        }

        return new ClaimMatch(predicates);
    }

    private static IReadOnlyList<Profile> MapProfiles(JsonElement el, List<string> errors)
    {
        var profiles = new List<Profile>();
        var index = 0;
        foreach (var item in el.EnumerateArray())
        {
            var context = $"spec.profiles[{index}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"'{context}' must be an object.");
                index++;
                continue;
            }

            var profile = MapProfile(item, context, errors);
            if (profile is not null)
            {
                profiles.Add(profile);
            }

            index++;
        }

        return profiles;
    }

    private static Profile? MapProfile(JsonElement el, string context, List<string> errors)
    {
        ValidateNoUnknownProperties(el, ProfileFields, context, errors);

        var name = RequireString(el, "name", $"{context}.name", errors);

        ClaimMatch? match = null;
        if (!el.TryGetProperty(MatchFieldName, out var matchEl))
        {
            errors.Add($"'{context}.match' is required.");
        }
        else
        {
            match = MapClaimMatch(matchEl, $"{context}.match", errors);
        }

        Grant? grant = null;
        if (!el.TryGetProperty("grant", out var grantEl))
        {
            errors.Add($"'{context}.grant' is required.");
        }
        else
        {
            grant = MapGrant(grantEl, $"{context}.grant", errors);
        }

        var nameClaim = RequireString(el, "nameClaim", $"{context}.nameClaim", errors);
        var autoAddSessionAccess = MapBoolWithDefault(el, "autoAddSessionAccess", defaultValue: false, $"{context}.autoAddSessionAccess", errors);
        var variables = MapVariables(el, context, errors);

        var publishTemplates = el.TryGetProperty("publishTemplates", out var ptEl)
            ? MapStringArray(ptEl, $"{context}.publishTemplates", errors)
            : [];
        var subscribeTemplates = el.TryGetProperty("subscribeTemplates", out var stEl)
            ? MapStringArray(stEl, $"{context}.subscribeTemplates", errors)
            : [];

        if (name is null || match is null || grant is null || nameClaim is null)
        {
            return null;
        }

        return new Profile(name, match, grant, nameClaim, variables, publishTemplates, subscribeTemplates, autoAddSessionAccess);
    }

    private static IReadOnlyList<TemplateVariable> MapVariables(JsonElement profileEl, string context, List<string> errors)
    {
        if (!profileEl.TryGetProperty("variables", out var varsEl))
        {
            return [];
        }

        if (varsEl.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"'{context}.variables' must be an array.");
            return [];
        }

        var variables = new List<TemplateVariable>();
        var index = 0;
        foreach (var v in varsEl.EnumerateArray())
        {
            var variable = MapVariable(v, $"{context}.variables[{index}]", errors);
            if (variable is not null)
            {
                variables.Add(variable);
            }

            index++;
        }

        return variables;
    }

    private static TemplateVariable? MapVariable(JsonElement el, string context, List<string> errors)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"'{context}' must be an object.");
            return null;
        }

        ValidateNoUnknownProperties(el, TemplateVariableFields, context, errors);

        var name = RequireString(el, "name", $"{context}.name", errors);
        var claim = RequireString(el, "claim", $"{context}.claim", errors);
        var required = MapBoolWithDefault(el, "required", defaultValue: false, $"{context}.required", errors);
        var transforms = MapTransforms(el, context, errors);

        if (name is null || claim is null)
        {
            return null;
        }

        return new TemplateVariable(name, claim, transforms, required);
    }

    private static IReadOnlyList<Transform> MapTransforms(JsonElement variableEl, string context, List<string> errors)
    {
        if (!variableEl.TryGetProperty("transforms", out var transformsEl))
        {
            return [];
        }

        if (transformsEl.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"'{context}.transforms' must be an array.");
            return [];
        }

        var transforms = new List<Transform>();
        var index = 0;
        foreach (var t in transformsEl.EnumerateArray())
        {
            if (Transform.TryParse(t, out var transform, out var transformError))
            {
                transforms.Add(transform);
            }
            else
            {
                errors.Add($"'{context}.transforms[{index}]': {transformError}");
            }

            index++;
        }

        return transforms;
    }

    private static string? RequireString(JsonElement obj, string propertyName, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el)
            || el.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(el.GetString()))
        {
            errors.Add($"'{context}' is required and must be a non-empty string.");
            return null;
        }

        return el.GetString();
    }

    private static string? MapNullableString(JsonElement obj, string propertyName, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.String)
        {
            errors.Add($"'{context}' must be a string or null.");
            return null;
        }

        return el.GetString();
    }

    private static bool MapBoolWithDefault(JsonElement obj, string propertyName, bool defaultValue, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el))
        {
            return defaultValue;
        }

        if (el.ValueKind != JsonValueKind.True && el.ValueKind != JsonValueKind.False)
        {
            errors.Add($"'{context}' must be a boolean.");
            return defaultValue;
        }

        return el.GetBoolean();
    }

    private static DurationString? RequireDuration(JsonElement obj, string propertyName, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.String)
        {
            errors.Add($"'{context}' is required and must be a duration string (e.g. '60s', '5m').");
            return null;
        }

        if (!DurationString.TryParse(el.GetString(), out var duration))
        {
            errors.Add($"'{context}' value '{el.GetString()}' is not a valid duration string.");
            return null;
        }

        return duration;
    }

    private static DurationString? MapNullableDuration(JsonElement obj, string propertyName, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.String || !DurationString.TryParse(el.GetString(), out var duration))
        {
            errors.Add($"'{context}' must be a duration string or null.");
            return null;
        }

        return duration;
    }

    private static DurationString MapDurationWithDefault(JsonElement obj, string propertyName, DurationString defaultValue, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return defaultValue;
        }

        if (el.ValueKind != JsonValueKind.String || !DurationString.TryParse(el.GetString(), out var duration))
        {
            errors.Add($"'{context}' must be a valid duration string.");
            return defaultValue;
        }

        return duration;
    }

    private static IReadOnlyList<string>? MapOptionalStringArray(JsonElement obj, string propertyName, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el))
        {
            return null;
        }

        return MapStringArray(el, context, errors);
    }

    private static IReadOnlyList<string> MapStringArray(JsonElement el, string context, List<string> errors)
    {
        if (el.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"'{context}' must be an array of strings.");
            return [];
        }

        var items = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                errors.Add($"'{context}' must contain only strings.");
                continue;
            }

            items.Add(item.GetString()!);
        }

        return items;
    }

    private static void ValidateNoUnknownProperties(JsonElement obj, HashSet<string> allowed, string context, List<string> errors)
    {
        var unknown = obj.EnumerateObject().Where(prop => !allowed.Contains(prop.Name));
        foreach (var prop in unknown)
        {
            errors.Add($"Unknown field '{prop.Name}' in {context} (strict schema, doc 09 §4).");
        }
    }
}
