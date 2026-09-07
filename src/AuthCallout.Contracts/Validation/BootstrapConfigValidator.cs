// -----------------------------------------------------------------------
// <copyright file="BootstrapConfigValidator.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Contracts.Validation;

/// <summary>
/// The bootstrap-layer half of <see cref="ConfigValidator"/> (doc 04 §Validation): seed
/// shapes, the assertion keyset (ADR 0014), source settings, JWKS/concurrency bounds, and
/// the anonymous global config. Split out of <see cref="ConfigValidator"/> to keep each
/// validator focused on one config layer's type surface.
/// </summary>
internal static class BootstrapConfigValidator
{
    /// <summary>Validates a <see cref="BootstrapConfig"/>. See <see cref="ConfigValidator.ValidateBootstrap"/>.</summary>
    internal static void Validate(BootstrapConfig bootstrap, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(bootstrap.NatsUrl))
        {
            errors.Add(new ValidationError("nats-url-required", "Bootstrap 'natsUrl' must not be empty."));
        }

        ValidateSecretRefShape(bootstrap.IssuerSeed, "issuerSeed", errors);
        if (bootstrap.ServiceUserSeed is { } serviceUserSeed)
        {
            ValidateSecretRefShape(serviceUserSeed, "serviceUserSeed", errors);
        }

        if (bootstrap.XkeySeed is { } xkeySeed)
        {
            ValidateSecretRefShape(xkeySeed, "xkeySeed", errors);
        }

        ValidateAssertionKeys(bootstrap.AssertionKeys, errors);
        ValidateSource(bootstrap.Source, errors);
        ValidateJwks(bootstrap.Jwks, errors);
        ValidateConcurrency(bootstrap.Concurrency, errors);
        ValidateAnonymous(bootstrap.Anonymous, errors);
    }

    private static void ValidateAssertionKeys(IReadOnlyList<AssertionKey>? keys, List<ValidationError> errors)
    {
        if (keys is null || keys.Count == 0)
        {
            return;
        }

        var activeCount = 0;
        var seenKids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (key.Active)
            {
                activeCount++;
            }

            if (string.Equals(key.Kid, AssertionKey.ReservedCurrentKid, StringComparison.Ordinal))
            {
                errors.Add(new ValidationError(
                    "reserved-kid",
                    $"Assertion key 'kid' must not be '{AssertionKey.ReservedCurrentKid}' (ADR 0013 reserves it as the current-pointer key)."));
            }

            if (!seenKids.Add(key.Kid))
            {
                errors.Add(new ValidationError("duplicate-kid", $"Assertion key 'kid' \"{key.Kid}\" is declared more than once; kids must be unique."));
            }

            ValidateSecretRefShape(key.Key, $"assertionKeys[{key.Kid}].key", errors);
        }

        if (activeCount != 1)
        {
            errors.Add(new ValidationError(
                "assertion-keyset-active-count",
                $"Exactly one assertion key must be 'active' (found {activeCount}, ADR 0014)."));
        }
    }

    private static void ValidateSource(SourceConfig source, List<ValidationError> errors)
    {
        switch (source.Kind)
        {
            case SourceKind.Files:
                {
                    ValidateFilesSource(source.FilesSettings, errors);
                    break;
                }

            case SourceKind.K8s:
                {
                    ValidateK8sSource(source.K8sSettings, errors);
                    break;
                }

            case SourceKind.Postgres:
                {
                    ValidatePostgresSource(source.PostgresSettings, errors);
                    break;
                }

            default:
                {
                    errors.Add(new ValidationError("unknown-source-kind", $"Unknown source kind '{source.Kind}'."));
                    break;
                }
        }

        ValidateSourceStalenessThresholds(source, errors);
    }

    private static void ValidateFilesSource(FilesSourceSettings? settings, List<ValidationError> errors)
    {
        if (settings is null || string.IsNullOrWhiteSpace(settings.Directory))
        {
            errors.Add(new ValidationError("files-directory-required", "Source kind is 'files' but no 'directory' was configured."));
        }
    }

#pragma warning disable S100 // "K8s" matches the repo-wide convention (AuthCallout.Config.K8s, SourceKind.K8s); "K8S" would be inconsistent.
    private static void ValidateK8sSource(K8sSourceSettings? settings, List<ValidationError> errors)
#pragma warning restore S100
    {
        if (settings is null || string.IsNullOrWhiteSpace(settings.OwnerLabelSelector))
        {
            errors.Add(new ValidationError("k8s-selector-required", "Source kind is 'k8s' but no owner label selector was configured."));
        }
    }

    private static void ValidatePostgresSource(PostgresSourceSettings? settings, List<ValidationError> errors)
    {
        if (settings is null)
        {
            errors.Add(new ValidationError("postgres-settings-required", "Source kind is 'postgres' but no postgres settings were configured."));
            return;
        }

        ValidateSecretRefShape(settings.ConnectionString, "source.postgres.connectionString", errors);
        if (string.IsNullOrWhiteSpace(settings.NotifyChannel))
        {
            errors.Add(new ValidationError("postgres-notify-channel-required", "Source 'postgres.notifyChannel' must not be empty."));
        }

        if (settings.PollInterval.Value <= TimeSpan.Zero)
        {
            errors.Add(new ValidationError("postgres-poll-interval-positive", "Source 'postgres.pollInterval' must be greater than zero."));
        }
    }

    private static void ValidateSourceStalenessThresholds(SourceConfig source, List<ValidationError> errors)
    {
        if (source.WarnStaleness.Value <= TimeSpan.Zero)
        {
            errors.Add(new ValidationError("warn-staleness-positive", "Source 'warnStaleness' must be greater than zero."));
        }

        if (source.MaxStaleness.Value <= source.WarnStaleness.Value)
        {
            errors.Add(new ValidationError(
                "max-staleness-exceeds-warn",
                $"Source 'maxStaleness' ({source.MaxStaleness}) must be greater than 'warnStaleness' ({source.WarnStaleness}, ADR 0017)."));
        }

        if (source.ReconnectBackoff.Value <= TimeSpan.Zero)
        {
            errors.Add(new ValidationError("reconnect-backoff-positive", "Source 'reconnectBackoff' must be greater than zero."));
        }
    }

    private static void ValidateJwks(JwksSettings jwks, List<ValidationError> errors)
    {
        if (jwks.RefreshInterval.Value <= TimeSpan.Zero)
        {
            errors.Add(new ValidationError("jwks-refresh-interval-positive", "'jwks.refreshInterval' must be greater than zero."));
        }

        if (jwks.FetchTimeout.Value <= TimeSpan.Zero)
        {
            errors.Add(new ValidationError("jwks-fetch-timeout-positive", "'jwks.fetchTimeout' must be greater than zero."));
        }
    }

    private static void ValidateConcurrency(ConcurrencySettings concurrency, List<ValidationError> errors)
    {
        if (concurrency.MaxDegreeOfParallelism <= 0)
        {
            errors.Add(new ValidationError("max-degree-of-parallelism-positive", "'concurrency.maxDegreeOfParallelism' must be greater than zero."));
        }

        if (concurrency.PerRequestTimeout.Value <= TimeSpan.Zero)
        {
            errors.Add(new ValidationError("per-request-timeout-positive", "'concurrency.perRequestTimeout' must be greater than zero."));
        }

        if (concurrency.QueueCapacity is { } capacity && capacity <= 0)
        {
            errors.Add(new ValidationError("queue-capacity-positive", "'concurrency.queueCapacity', when set, must be greater than zero."));
        }
    }

    private static void ValidateAnonymous(AnonymousGlobalConfig anonymous, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(anonymous.TargetAccount))
        {
            errors.Add(new ValidationError("anonymous-target-account-required", "'anonymous.targetAccount' must not be empty."));
        }

        if (anonymous.Expiry.Value <= TimeSpan.Zero)
        {
            errors.Add(new ValidationError("anonymous-expiry-positive", "'anonymous.expiry' must be greater than zero."));
        }
    }

    private static void ValidateSecretRefShape(SecretRef secretRef, string fieldName, List<ValidationError> errors)
    {
        if (!SecretRefShape.IsWellFormed(secretRef))
        {
            errors.Add(new ValidationError(
                "malformed-secret-ref",
                $"'{fieldName}' is a malformed secret reference: kind '{secretRef.Kind}' requires locator fields that are missing."));
        }
    }
}
