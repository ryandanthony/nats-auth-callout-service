// -----------------------------------------------------------------------
// <copyright file="AppSlug.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// A stable, collision-free, slugified application name (ADR 0013 §Naming rules,
/// §App-slug and key-safety transform) and the single source of truth for the physical
/// KV bucket names derived from it (ADR 0013 §Mapping table). Every packet that needs a
/// bucket name (the identity store writer, the grant minter, the discovery-descriptor
/// emitter) derives it from an <see cref="AppSlug"/> instance rather than re-implementing
/// the slugification or naming rule, so grants and writes can never diverge.
/// </summary>
public readonly struct AppSlug : IEquatable<AppSlug>
{
    private const int MaxLengthValue = 63;

    /// <summary>
    /// Gets the length budget for a slug (ADR 0013: "Reject... if the result is... exceeds the
    /// length budget"). Chosen so every physical bucket name this ADR derives — the longest
    /// being <c>callout_apps_{slug}_oidc_tokens</c>, a 26-character fixed frame — stays
    /// comfortably within NATS's practical stream/bucket-name limits with headroom for
    /// operators to read the name at a glance. Mirrors the common DNS-label length (63).
    /// </summary>
    /// <value>The maximum slug length, <c>63</c>.</value>
    public static int MaxLength => MaxLengthValue;

    private const string BucketPrefix = "callout_apps_";

    private AppSlug(string value)
    {
        Value = value;
    }

    /// <summary>Gets the slug text.</summary>
    /// <value>The slug text.</value>
    public string Value { get; }

    public static bool operator ==(AppSlug left, AppSlug right) => left.Equals(right);

    public static bool operator !=(AppSlug left, AppSlug right) => !left.Equals(right);

    /// <summary>
    /// Slugifies an application name per ADR 0013's app-slug transform:
    /// lowercase, replace any run of characters outside <c>[a-z0-9-]</c> with a single
    /// <c>-</c>, trim leading/trailing separators, then reject if empty or over
    /// <see cref="MaxLength"/>.
    /// </summary>
    /// <param name="applicationName">The application's authored <c>name</c>.</param>
    /// <exception cref="FormatException">
    /// The slugified result is empty (the name contained no <c>[a-z0-9-]</c> characters) or
    /// exceeds <see cref="MaxLength"/>.
    /// </exception>
    /// <returns>The slugified application name.</returns>
    public static AppSlug From(string applicationName)
    {
        if (!TryFrom(applicationName, out var slug, out var error))
        {
            throw new FormatException(error);
        }

        return slug;
    }

    /// <summary>Attempts to slugify an application name. See <see cref="From"/> for the transform.</summary>
    /// <param name="applicationName">The application's authored <c>name</c>.</param>
    /// <param name="slug">The slugified result, when this returns true.</param>
    /// <param name="error">A description of why slugification failed, when this returns false.</param>
    /// <returns>True when slugification succeeded.</returns>
#pragma warning disable CA1021 // Two 'out' params (slug + error) mirror the .NET TryParse pattern generalized with a diagnostic; a wrapper result type would be worse ergonomics for every call site (validator, providers).
    public static bool TryFrom(string? applicationName, out AppSlug slug, out string? error)
#pragma warning restore CA1021
    {
        slug = default;

        if (string.IsNullOrEmpty(applicationName))
        {
            error = "Application name must not be empty.";
            return false;
        }

        var builder = new StringBuilder(applicationName.Length);
        var lastWasSeparator = false;
        foreach (var ch in applicationName)
        {
#pragma warning disable S4040 // ADR 0013 requires lowercase output (the slug itself, not a comparison); ToUpperInvariant would produce the wrong case.
            var lower = char.ToLowerInvariant(ch);
#pragma warning restore S4040
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(lower);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
            else
            {
                // Run of non-safe characters already collapsed to a single trailing '-'; skip.
            }
        }

        var slugged = builder.ToString().Trim('-');

        if (slugged.Length == 0)
        {
            error = $"Application name '{applicationName}' slugifies to an empty string (ADR 0013): it must contain at least one letter or digit.";
            return false;
        }

        if (slugged.Length > MaxLength)
        {
            error = $"Application name '{applicationName}' slugifies to '{slugged}' ({slugged.Length} chars), exceeding the {MaxLength}-character slug length budget (ADR 0013).";
            return false;
        }

        slug = new AppSlug(slugged);
        error = null;
        return true;
    }

    /// <summary>Gets the physical OIDC token-store bucket name: <c>callout_apps_{slug}_oidc_tokens</c> (ADR 0013, ADR 0010).</summary>
    /// <value>The physical OIDC token-store bucket name.</value>
    public string OidcTokensBucket => $"{BucketPrefix}{Value}_oidc_tokens";

    /// <summary>Gets the physical id-lookup/assertions bucket name: <c>callout_apps_{slug}_assertions</c> (ADR 0013, ADR 0010/0011).</summary>
    /// <value>The physical id-lookup/assertions bucket name.</value>
    public string AssertionsBucket => $"{BucketPrefix}{Value}_assertions";

    /// <summary>Gets the physical discovery-descriptor bucket name: <c>callout_apps_{slug}_discovery</c> (ADR 0013, ADR 0012).</summary>
    /// <value>The physical discovery-descriptor bucket name.</value>
    public string DiscoveryBucket => $"{BucketPrefix}{Value}_discovery";

    /// <summary>Gets the global assertion-keys bucket name: <c>callout_assertion_keys</c> (ADR 0013, ADR 0011). Not per-application; provided here for co-location with the other bucket-name derivations.</summary>
    /// <value>The global assertion-keys bucket name.</value>
    public static string AssertionKeysBucket => "callout_assertion_keys";

    /// <summary>Gets the reserved key, within <see cref="AssertionKeysBucket"/>, pointing at the active signer's <c>kid</c> (ADR 0013/0014). Forbidden as an actual <c>kid</c> value.</summary>
    /// <value>The reserved current-pointer key name.</value>
    public static string AssertionKeysCurrentKey => "current";

    /// <summary>Gets the reserved discovery-descriptor key within <see cref="DiscoveryBucket"/> (ADR 0013).</summary>
    /// <value>The reserved discovery-descriptor key name.</value>
    public static string DiscoveryDescriptorKey => "descriptor";

    /// <summary>Gets the physical <c>$KV.</c> subject granting read of the whole application token bucket (ADR 0013 §Grant subjects), used for <see cref="Profile.AutoAddSessionAccess"/>.</summary>
    /// <value>The physical token-bucket read subject.</value>
    public string OidcTokensReadSubject => $"$KV.{OidcTokensBucket}.>";

    /// <summary>The physical <c>$KV.</c> subject for a single assertion self-lookup entry (ADR 0013 §Grant subjects), keyed by the client's own <c>jti</c> (or <c>sub</c> fallback, doc 07).</summary>
    /// <param name="jtiOrSubjectFallback">The client's own <c>jti</c> (or <c>sub</c> fallback). Derived via <see cref="DeriveKeySegment"/> before use, since <c>jti</c> comes from the token and must be both key-safe and collision-free (ADR 0013).</param>
    /// <returns>The physical self-lookup subject for that key.</returns>
    public string AssertionSelfLookupSubject(string jtiOrSubjectFallback)
    {
        ArgumentException.ThrowIfNullOrEmpty(jtiOrSubjectFallback);
        return $"$KV.{AssertionsBucket}.{DeriveKeySegment(jtiOrSubjectFallback)}";
    }

    /// <summary>
    /// The single source of truth for ADR 0013's key-safety transform applied to a dynamic
    /// key segment (<c>jti</c>, <c>id</c>, <c>kid</c>) at write time: lowercase, then replace
    /// any run of characters outside <c>[a-z0-9-]</c> with a single <c>_</c>. Both the granted
    /// self-lookup subject (resolution pipeline) and the identity-store write key (store writer)
    /// pass a segment through this method, so a client's granted subject and the key actually
    /// written can never diverge for a <c>jti</c> containing unsafe characters.
    /// </summary>
    /// <param name="segment">The raw dynamic key segment (e.g. a token <c>jti</c>).</param>
    /// <returns>The key-safe segment.</returns>
    /// <exception cref="ArgumentException"><paramref name="segment"/> is null or empty.</exception>
    public static string SanitizeKeySegment(string segment)
    {
        ArgumentException.ThrowIfNullOrEmpty(segment);

        var builder = new StringBuilder(segment.Length);
        var lastWasSeparator = false;
        foreach (var ch in segment)
        {
#pragma warning disable S4040 // ADR 0013 requires lowercase output (a key segment, not a comparison); ToUpperInvariant would produce the wrong case.
            var lower = char.ToLowerInvariant(ch);
#pragma warning restore S4040
            if (IsKeySafe(lower))
            {
                builder.Append(lower);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append('_');
                lastWasSeparator = true;
            }
            else
            {
                // Run of unsafe characters already collapsed to a single '_'; skip.
            }
        }

        return builder.ToString();
    }

    private static bool IsKeySafe(char lower) =>
        lower is >= 'a' and <= 'z' or >= '0' and <= '9' || lower == '-';

    /// <summary>The number of leading hex characters of the SHA-256 digest appended by <see cref="DeriveKeySegment"/>.</summary>
    private const int KeyDigestLength = 16;

    /// <summary>
    /// The single source of truth for deriving a <b>collision-free</b>, key-safe segment from
    /// a dynamic, token-supplied value (<c>jti</c>, <c>sub</c>): <see cref="SanitizeKeySegment"/>
    /// of the raw value, plus an <c>_</c>-joined 16-hex-character SHA-256 digest of the raw
    /// value's UTF-8 bytes. The sanitized half keeps the segment operator-readable; the digest
    /// half makes distinct raw values yield distinct segments even when sanitization would
    /// collide them (e.g. <c>User.A</c> vs <c>user_a</c> vs <c>USER A</c>), so one principal's
    /// granted subject or written key can never alias another's. Both the granted self-lookup
    /// subject (<see cref="AssertionSelfLookupSubject"/>), the identity-store write key, the
    /// scoped client-inbox grant (<see cref="ClientInboxSubscribeSubject"/>), and client-side
    /// consumers derive segments through this one method.
    /// </summary>
    /// <param name="segment">The raw dynamic key segment (e.g. a token <c>jti</c> or <c>sub</c>).</param>
    /// <returns>The collision-free, key-safe segment.</returns>
    /// <exception cref="ArgumentException"><paramref name="segment"/> is null or empty.</exception>
    public static string DeriveKeySegment(string segment)
    {
        ArgumentException.ThrowIfNullOrEmpty(segment);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(segment)))[..KeyDigestLength];
        return $"{SanitizeKeySegment(segment)}_{digest}";
    }

    /// <summary>Gets the physical <c>$KV.</c> subject granting read of the whole assertion-keys bucket (ADR 0013 §Grant subjects), added to every authenticated grant.</summary>
    /// <value>The physical assertion-keys read subject.</value>
    public static string AssertionKeysReadSubject => $"$KV.{AssertionKeysBucket}.>";

    /// <summary>Gets the physical <c>$KV.</c> subject for this application's discovery descriptor entry (ADR 0013 §Grant subjects), added to every authenticated grant in this application.</summary>
    /// <value>The physical discovery-descriptor read subject.</value>
    public string DiscoveryReadSubject => $"$KV.{DiscoveryBucket}.{DiscoveryDescriptorKey}";

    /// <summary>
    /// The subscribe subject granting this connection's <b>own, scoped</b> NATS request-reply
    /// inbox: <c>_INBOX.{slug}.{derived-principal-key}.&gt;</c>. A JetStream KV client
    /// (<c>NatsKVContext</c>) receives stream-info / direct-get replies on its connection
    /// inbox, so any grant that reads at least one KV bucket needs an inbox subscribe — but
    /// granting the account-wide <c>_INBOX.&gt;</c> wildcard would let every authenticated
    /// client read every other client's request replies in the account. Instead the grant is
    /// scoped to a prefix derived from the application slug and the connection's principal
    /// (<c>sub</c>, falling back to <c>azp</c>, then the minted name), and the client must
    /// configure the matching custom inbox prefix (<c>NatsOpts.InboxPrefix</c> in NATS.NET,
    /// <c>custom_inbox_prefix</c> in other clients): <c>_INBOX.{slug}.{DeriveKeySegment(principal)}</c>.
    /// Connections sharing a principal share a prefix (same identity); everyone else's inbox
    /// is out of reach.
    /// </summary>
    /// <param name="principalKey">The connection's principal identifier (<c>sub</c>/<c>azp</c>-style, or the minted name as a last resort).</param>
    /// <returns>The scoped client inbox subscribe subject.</returns>
    /// <exception cref="ArgumentException"><paramref name="principalKey"/> is null or empty.</exception>
    public string ClientInboxSubscribeSubject(string principalKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(principalKey);
        return $"{ClientInboxPrefix(principalKey)}.>";
    }

    /// <summary>
    /// The custom inbox prefix a client must set (<c>NatsOpts.InboxPrefix</c> /
    /// <c>custom_inbox_prefix</c>) so its request-reply inboxes land inside the subjects
    /// <see cref="ClientInboxSubscribeSubject"/> grants it: <c>_INBOX.{slug}.{DeriveKeySegment(principal)}</c>.
    /// </summary>
    /// <param name="principalKey">The connection's principal identifier, matching what the callout granted.</param>
    /// <returns>The inbox prefix to configure on the client connection.</returns>
    /// <exception cref="ArgumentException"><paramref name="principalKey"/> is null or empty.</exception>
    public string ClientInboxPrefix(string principalKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(principalKey);
        return $"_INBOX.{Value}.{DeriveKeySegment(principalKey)}";
    }

    /// <summary>
    /// The JetStream API subjects a KV client must be able to <b>publish</b> to in order to
    /// read entries from the bucket whose backing stream is <c>KV_{bucket}</c>: stream-info
    /// (bucket existence/config) and direct-get (entry read via <c>allow_direct</c>). Scoped to
    /// the single backing stream, not the account-wide <c>$JS.API.&gt;</c>, so a grant only
    /// reaches the buckets it was actually granted <c>$KV</c> read on. Companion to
    /// <see cref="ClientInboxSubscribeSubject"/> (the reply path).
    /// </summary>
    /// <param name="bucket">The physical KV bucket name (e.g. <see cref="AssertionsBucket"/>).</param>
    /// <returns>The JetStream API publish subjects for that bucket's backing stream.</returns>
    public static IReadOnlyList<string> JetStreamReadApiPublishSubjects(string bucket)
    {
        ArgumentException.ThrowIfNullOrEmpty(bucket);
        var stream = $"KV_{bucket}";
        return [$"$JS.API.STREAM.INFO.{stream}", $"$JS.API.DIRECT.GET.{stream}"];
    }

    /// <inheritdoc />
    public bool Equals(AppSlug other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AppSlug other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Value;
}
