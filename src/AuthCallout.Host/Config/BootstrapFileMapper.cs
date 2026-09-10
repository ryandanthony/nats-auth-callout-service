// -----------------------------------------------------------------------
// <copyright file="BootstrapFileMapper.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Host.Config;

/// <summary>
/// Maps the bootstrap file's parsed JSON representation into <see cref="BootstrapConfig"/>
/// (doc 09 §Bootstrap file, doc 04 §Two layers). Mirrors
/// <c>AuthCallout.Config.Files.Parsing.ApplicationFileMapper</c>'s shape (operate purely on
/// <see cref="JsonElement"/> so JSON and YAML bootstrap files, the latter converted by
/// <c>AuthCallout.Config.Files.Yaml.YamlToJsonConverter</c>, go through identical mapping
/// code) but lives in <c>AuthCallout.Host</c> because the bootstrap file — unlike per-app
/// routing files — is P13's own concern (doc 04: "loaded once at startup", never a
/// hot-reloaded <see cref="AuthCallout.Contracts.Seams.IRoutingConfigProvider"/>). Strict:
/// every object level rejects unknown properties (matching the bootstrap JSON Schema's
/// <c>additionalProperties: false</c>).
/// </summary>
public static class BootstrapFileMapper
{
    /// <summary>Gets the only bootstrap file <c>schemaVersion</c> this service currently supports.</summary>
    /// <value>The only supported bootstrap file <c>schemaVersion</c>, <c>1</c>.</value>
    public static int SupportedSchemaVersion => 1;

    private static readonly HashSet<string> TopLevelFields = new(StringComparer.Ordinal)
    {
        "schemaVersion", "natsUrl", "issuerSeed", "serviceUserSeed", "xkeySeed",
        "assertionKeys", "source", "jwks", "concurrency", "anonymous",
    };

    private static readonly HashSet<string> SecretRefFields = new(StringComparer.Ordinal)
    {
        "kind", "value", "path", "name", "key",
    };

    private static readonly HashSet<string> AssertionKeyFields = new(StringComparer.Ordinal) { "kid", "key", "active" };

    private static readonly HashSet<string> SourceFields = new(StringComparer.Ordinal)
    {
        "kind", "directory", "labelSelector", "connectionString", "pollInterval",
        "notifyChannel", "warnStaleness", "maxStaleness", "reconnectBackoff",
    };

    private static readonly HashSet<string> JwksFields = new(StringComparer.Ordinal)
    {
        "refreshInterval", "fetchTimeout", "prewarmRequired",
    };

    private static readonly HashSet<string> ConcurrencyFields = new(StringComparer.Ordinal)
    {
        "maxDegreeOfParallelism", "perRequestTimeout", "queueCapacity",
    };

    private static readonly HashSet<string> AnonymousFields = new(StringComparer.Ordinal)
    {
        "targetAccount", "expiry", "events",
    };

    private static readonly HashSet<string> EventFields = new(StringComparer.Ordinal) { "success", "failure" };

    /// <summary>Attempts to map a parsed bootstrap file into <see cref="BootstrapConfig"/>.</summary>
    /// <param name="root">The file's root JSON element (an object).</param>
    /// <returns>The mapping result: a populated <see cref="BootstrapConfig"/>, or every problem found.</returns>
    public static BootstrapMapResult TryMap(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return BootstrapMapResult.Failure("Bootstrap file root must be a JSON object.");
        }

        var errors = new List<string>();
        ValidateNoUnknownProperties(root, TopLevelFields, "bootstrap file", errors);
        ValidateSchemaVersion(root, errors);

        var natsUrl = RequireString(root, "natsUrl", "natsUrl", errors);
        var issuerSeed = RequireSecretRef(root, "issuerSeed", "issuerSeed", errors);
        var serviceUserSeed = MapOptionalSecretRef(root, "serviceUserSeed", "serviceUserSeed", errors);
        var xkeySeed = MapOptionalSecretRef(root, "xkeySeed", "xkeySeed", errors);
        var assertionKeys = MapAssertionKeys(root, errors);
        var source = MapSource(root, errors);
        var jwks = MapJwks(root, errors);
        var concurrency = MapConcurrency(root, errors);
        var anonymous = MapAnonymous(root, errors);

        if (errors.Count > 0 || !RequiredFieldsPresent(natsUrl, issuerSeed, source, jwks, concurrency, anonymous))
        {
            return BootstrapMapResult.Failure(string.Join(" ", errors));
        }

        var bootstrap = new BootstrapConfig(natsUrl!, issuerSeed!, serviceUserSeed, xkeySeed, assertionKeys, source!, jwks!, concurrency!, anonymous!);
        return BootstrapMapResult.Success(bootstrap);
    }

    private static bool RequiredFieldsPresent(
        string? natsUrl, SecretRef? issuerSeed, SourceConfig? source, JwksSettings? jwks, ConcurrencySettings? concurrency, AnonymousGlobalConfig? anonymous)
    {
        var topLevelPresent = natsUrl is not null && issuerSeed is not null;
        var sectionsPresent = source is not null && jwks is not null && concurrency is not null && anonymous is not null;
        return topLevelPresent && sectionsPresent;
    }

    private static void ValidateSchemaVersion(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("schemaVersion", out var el))
        {
            errors.Add("'schemaVersion' is required.");
            return;
        }

        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var version) || version != SupportedSchemaVersion)
        {
            errors.Add($"'schemaVersion' must be the integer {SupportedSchemaVersion}.");
        }
    }

    private static SecretRef? RequireSecretRef(JsonElement obj, string propertyName, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"'{context}' is required and must be a secret reference object.");
            return null;
        }

        return MapSecretRef(el, context, errors);
    }

    private static SecretRef? MapOptionalSecretRef(JsonElement obj, string propertyName, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"'{context}' must be a secret reference object or null.");
            return null;
        }

        return MapSecretRef(el, context, errors);
    }

    private static SecretRef? MapSecretRef(JsonElement el, string context, List<string> errors)
    {
        ValidateNoUnknownProperties(el, SecretRefFields, context, errors);

        var kindText = RequireString(el, "kind", $"{context}.kind", errors);
        var value = MapNullableString(el, "value", $"{context}.value", errors);
        var path = MapNullableString(el, "path", $"{context}.path", errors);
        var name = MapNullableString(el, "name", $"{context}.name", errors);
        var key = MapNullableString(el, "key", $"{context}.key", errors);

        if (kindText is null)
        {
            return null;
        }

        if (!TryParseSecretRefKind(kindText, out var kind))
        {
            errors.Add($"'{context}.kind' value '{kindText}' is not one of inline|file|k8sSecret|vault.");
            return null;
        }

        return new SecretRef(kind, value, path, name, key);
    }

    private static readonly IReadOnlyDictionary<string, SecretRefKind> SecretRefKindsByName = new Dictionary<string, SecretRefKind>(StringComparer.Ordinal)
    {
        ["inline"] = SecretRefKind.Inline,
        ["file"] = SecretRefKind.File,
        ["k8sSecret"] = SecretRefKind.K8sSecret,
        ["vault"] = SecretRefKind.Vault,
    };

    private static bool TryParseSecretRefKind(string text, out SecretRefKind kind) => SecretRefKindsByName.TryGetValue(text, out kind);

    private static IReadOnlyList<AssertionKey>? MapAssertionKeys(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("assertionKeys", out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.Array)
        {
            errors.Add("'assertionKeys' must be an array.");
            return null;
        }

        var keys = new List<AssertionKey>();
        var index = 0;
        foreach (var item in el.EnumerateArray())
        {
            var context = $"assertionKeys[{index}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"'{context}' must be an object.");
                index++;
                continue;
            }

            ValidateNoUnknownProperties(item, AssertionKeyFields, context, errors);
            var kid = RequireString(item, "kid", $"{context}.kid", errors);
            var key = RequireSecretRef(item, "key", $"{context}.key", errors);
            var active = RequireBool(item, "active", $"{context}.active", errors);

            if (kid is not null && key is not null && active is not null)
            {
                keys.Add(new AssertionKey(kid, key, active.Value));
            }

            index++;
        }

        return keys;
    }

    private static SourceConfig? MapSource(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("source", out var el) || el.ValueKind != JsonValueKind.Object)
        {
            errors.Add("'source' is required and must be an object.");
            return null;
        }

        ValidateNoUnknownProperties(el, SourceFields, "source", errors);

        var kindText = RequireString(el, "kind", "source.kind", errors);
        var warnStaleness = RequireDuration(el, "warnStaleness", "source.warnStaleness", errors);
        var maxStaleness = RequireDuration(el, "maxStaleness", "source.maxStaleness", errors);
        var reconnectBackoff = RequireDuration(el, "reconnectBackoff", "source.reconnectBackoff", errors);

        if (kindText is null || warnStaleness is null || maxStaleness is null || reconnectBackoff is null)
        {
            return null;
        }

        return kindText switch
        {
            "files" => MapFilesSource(el, warnStaleness.Value, maxStaleness.Value, reconnectBackoff.Value, errors),
            "k8s" => MapK8SSource(el, warnStaleness.Value, maxStaleness.Value, reconnectBackoff.Value, errors),
            "postgres" => MapPostgresSource(el, warnStaleness.Value, maxStaleness.Value, reconnectBackoff.Value, errors),
            _ => Fail($"'source.kind' value '{kindText}' is not one of files|k8s|postgres.", errors),
        };
    }

    private static SourceConfig? MapFilesSource(JsonElement el, DurationString warn, DurationString max, DurationString backoff, List<string> errors)
    {
        var directory = RequireString(el, "directory", "source.directory", errors);
        return directory is null ? null : SourceConfig.Files(new FilesSourceSettings(directory), warn, max, backoff);
    }

    private static SourceConfig? MapK8SSource(JsonElement el, DurationString warn, DurationString max, DurationString backoff, List<string> errors)
    {
        var labelSelector = RequireString(el, "labelSelector", "source.labelSelector", errors);
        return labelSelector is null ? null : SourceConfig.K8s(new K8sSourceSettings(labelSelector), warn, max, backoff);
    }

    private static SourceConfig? MapPostgresSource(JsonElement el, DurationString warn, DurationString max, DurationString backoff, List<string> errors)
    {
        var connectionString = RequireSecretRef(el, "connectionString", "source.connectionString", errors);
        var pollInterval = RequireDuration(el, "pollInterval", "source.pollInterval", errors);
        var notifyChannel = MapNullableString(el, "notifyChannel", "source.notifyChannel", errors) ?? PostgresSourceSettings.DefaultNotifyChannel;

        if (connectionString is null || pollInterval is null)
        {
            return null;
        }

        return SourceConfig.Postgres(new PostgresSourceSettings(connectionString, pollInterval.Value, notifyChannel), warn, max, backoff);
    }

    private static JwksSettings? MapJwks(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("jwks", out var el) || el.ValueKind != JsonValueKind.Object)
        {
            errors.Add("'jwks' is required and must be an object.");
            return null;
        }

        ValidateNoUnknownProperties(el, JwksFields, "jwks", errors);
        var refreshInterval = RequireDuration(el, "refreshInterval", "jwks.refreshInterval", errors);
        var fetchTimeout = RequireDuration(el, "fetchTimeout", "jwks.fetchTimeout", errors);
        var prewarmRequired = RequireBool(el, "prewarmRequired", "jwks.prewarmRequired", errors);

        if (refreshInterval is null || fetchTimeout is null || prewarmRequired is null)
        {
            return null;
        }

        return new JwksSettings(refreshInterval.Value, fetchTimeout.Value, prewarmRequired.Value);
    }

    private static ConcurrencySettings? MapConcurrency(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("concurrency", out var el) || el.ValueKind != JsonValueKind.Object)
        {
            errors.Add("'concurrency' is required and must be an object.");
            return null;
        }

        ValidateNoUnknownProperties(el, ConcurrencyFields, "concurrency", errors);
        var maxDop = RequireInt(el, "maxDegreeOfParallelism", "concurrency.maxDegreeOfParallelism", errors);
        var perRequestTimeout = RequireDuration(el, "perRequestTimeout", "concurrency.perRequestTimeout", errors);
        var queueCapacity = MapOptionalInt(el, "queueCapacity", "concurrency.queueCapacity", errors);

        if (maxDop is null || perRequestTimeout is null)
        {
            return null;
        }

        return new ConcurrencySettings(maxDop.Value, perRequestTimeout.Value, queueCapacity);
    }

    private static AnonymousGlobalConfig? MapAnonymous(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("anonymous", out var el) || el.ValueKind != JsonValueKind.Object)
        {
            errors.Add("'anonymous' is required and must be an object.");
            return null;
        }

        ValidateNoUnknownProperties(el, AnonymousFields, "anonymous", errors);
        var targetAccount = RequireString(el, "targetAccount", "anonymous.targetAccount", errors);
        var expiry = RequireDuration(el, "expiry", "anonymous.expiry", errors);
        var events = el.TryGetProperty("events", out var eventsEl) && eventsEl.ValueKind != JsonValueKind.Null
            ? MapEventSubjects(eventsEl, "anonymous.events", errors)
            : null;

        if (targetAccount is null || expiry is null)
        {
            return null;
        }

        return new AnonymousGlobalConfig(targetAccount, expiry.Value, events);
    }

    private static EventSubjects MapEventSubjects(JsonElement el, string context, List<string> errors)
    {
        ValidateNoUnknownProperties(el, EventFields, context, errors);
        var success = MapNullableString(el, "success", $"{context}.success", errors);
        var failure = MapNullableString(el, "failure", $"{context}.failure", errors);
        return new EventSubjects(success, failure);
    }

    private static string? RequireString(JsonElement obj, string propertyName, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(el.GetString()))
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

    private static bool? RequireBool(JsonElement obj, string propertyName, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el) || (el.ValueKind != JsonValueKind.True && el.ValueKind != JsonValueKind.False))
        {
            errors.Add($"'{context}' is required and must be a boolean.");
            return null;
        }

        return el.GetBoolean();
    }

    private static int? RequireInt(JsonElement obj, string propertyName, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var value) || value < 1)
        {
            errors.Add($"'{context}' is required and must be a positive integer.");
            return null;
        }

        return value;
    }

    private static int? MapOptionalInt(JsonElement obj, string propertyName, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var value) || value < 1)
        {
            errors.Add($"'{context}' must be a positive integer.");
            return null;
        }

        return value;
    }

    private static DurationString? RequireDuration(JsonElement obj, string propertyName, string context, List<string> errors)
    {
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.String || !DurationString.TryParse(el.GetString(), out var duration))
        {
            errors.Add($"'{context}' is required and must be a duration string (e.g. '60s', '5m').");
            return null;
        }

        return duration;
    }

    private static SourceConfig? Fail(string message, List<string> errors)
    {
        errors.Add(message);
        return null;
    }

    private static void ValidateNoUnknownProperties(JsonElement obj, HashSet<string> allowed, string context, List<string> errors)
    {
        var unknown = obj.EnumerateObject().Where(prop => !allowed.Contains(prop.Name));
        foreach (var prop in unknown)
        {
            errors.Add($"Unknown field '{prop.Name}' in {context} (strict schema, doc 09 bootstrap file).");
        }
    }
}
