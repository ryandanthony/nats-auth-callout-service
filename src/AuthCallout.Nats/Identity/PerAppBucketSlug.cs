// -----------------------------------------------------------------------
// <copyright file="PerAppBucketSlug.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Nats.Identity;

/// <summary>
/// Extracts the application slug from a physical per-application bucket name (ADR 0013
/// §Mapping table: <c>callout_apps_{app-slug}_oidc_tokens</c>,
/// <c>callout_apps_{app-slug}_assertions</c>, <c>callout_apps_{app-slug}_discovery</c>),
/// the inverse of <see cref="AuthCallout.Contracts.Config.AppSlug"/>'s forward derivation.
/// Used only by <see cref="BucketProvisioner"/>'s orphan scan (D16) — nothing else needs to
/// go from bucket name back to slug.
/// </summary>
internal static class PerAppBucketSlug
{
    private const string BucketPrefix = "callout_apps_";

    private static readonly string[] KnownSuffixes = ["_oidc_tokens", "_assertions", "_discovery"];

    /// <summary>
    /// Attempts to extract the application slug from a physical bucket name. Only matches
    /// the three known per-application bucket suffixes; the global
    /// <c>callout_assertion_keys</c> bucket (no <c>callout_apps_</c> prefix) and any
    /// unrecognized bucket name both yield null.
    /// </summary>
    /// <param name="bucket">The physical bucket name.</param>
    /// <returns>The extracted slug, or null when <paramref name="bucket"/> is not a recognized per-application bucket.</returns>
    internal static string? TryExtract(string bucket)
    {
        ArgumentNullException.ThrowIfNull(bucket);

        if (!bucket.StartsWith(BucketPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var withoutPrefix = bucket[BucketPrefix.Length..];
        foreach (var suffix in KnownSuffixes)
        {
            if (withoutPrefix.EndsWith(suffix, StringComparison.Ordinal) && withoutPrefix.Length > suffix.Length)
            {
                return withoutPrefix[..^suffix.Length];
            }
        }

        return null;
    }
}
