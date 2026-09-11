// -----------------------------------------------------------------------
// <copyright file="NatsServerConfigBuilder.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace AuthCallout.Integration.Support;

/// <summary>
/// Builds a real <c>nats-server</c> config-mode config file (config mode, not operator mode,
/// per <see href="https://docs.nats.io/learn/security/auth-callout"/>) wiring up
/// <c>auth_callout</c> exactly as doc 05/08 describe: the callout runs its own NATS
/// connection as the sole <c>auth_users</c> member of a dedicated <c>AUTH</c> account (doc 08
/// §Why the auth service runs in its own account), authenticated by NKey (matching
/// <see cref="AuthCallout.Host.Wiring.ComponentGraph"/>'s <c>BuildNatsOpts</c>: seed + NKey,
/// never a password). Every target account a scenario needs (<c>NOTES</c>, <c>TASKS</c>,
/// <c>ANON</c>) imports <c>AUTH</c>'s <c>$JS.API.&gt;</c> service and <c>$KV.&gt;</c> stream
/// exports rather than declaring its own local JetStream domain (doc 07's discovery/keys/
/// token/assertion KV buckets are created via the callout's own AUTH-account connection, so a
/// client minted into another account cannot read them without this cross-account share — see
/// <see cref="AppendImportingAccount"/>). The minted user JWT names the account, the server
/// places the connection there (doc 03 §Mint and respond) — plus the mandatory <c>SYS</c>
/// system account.
/// </summary>
public static class NatsServerConfigBuilder
{
#pragma warning disable S2339 // Kept const (matching the repo's existing named-constant pattern, e.g. PostgresSourceSettings.DefaultNotifyChannel); these are config-literal names, not mutable state.
    /// <summary>The account name the callout's own service-user connection lives in (isolated per doc 08).</summary>
    public const string AuthAccountName = "AUTH";

    /// <summary>The default target account for the <c>notes</c> sample application.</summary>
    public const string NotesAccountName = "NOTES";

    /// <summary>The default target account for the <c>tasks</c> sample application.</summary>
    public const string TasksAccountName = "TASKS";

    /// <summary>The global anonymous-grant target account (doc 02 §Anonymous grant is a union).</summary>
    public const string AnonAccountName = "ANON";
#pragma warning restore S2339

    /// <summary>
    /// Builds the config text.
    /// </summary>
    /// <param name="issuerPublicKey">The account-prefixed NKey public key signing every minted user JWT and authorization response (bootstrap <c>issuerSeed</c>'s public half).</param>
    /// <param name="serviceUserPublicKey">The user-prefixed NKey public key the callout's own connection authenticates with (bootstrap <c>serviceUserSeed</c>'s public half); the sole <c>auth_users</c> exemption.</param>
    /// <param name="jetStreamStoreDirectory">The JetStream store directory inside the container.</param>
    /// <param name="extraTargetAccounts">Any additional empty target accounts a scenario needs beyond the defaults.</param>
    /// <param name="xkeyPublicKey">When set, configures <c>auth_callout.xkey</c> so the server seals requests/replies to this curve public key (ADR 0018).</param>
    /// <param name="calloutTimeout">Overrides the default 2s auth-callout wait (doc "Pitfalls": set deliberately). Null keeps the server default.</param>
    /// <returns>The complete config file text.</returns>
    public static string Build(
        string issuerPublicKey,
        string serviceUserPublicKey,
        string jetStreamStoreDirectory,
        IReadOnlyList<string>? extraTargetAccounts = null,
        string? xkeyPublicKey = null,
        TimeSpan? calloutTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerPublicKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceUserPublicKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(jetStreamStoreDirectory);

        var sb = new StringBuilder();
        sb.AppendLine("jetstream {");
        sb.AppendLine(Inv($"  store_dir: \"{jetStreamStoreDirectory}\""));
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("accounts {");
        AppendAuthAccount(sb, serviceUserPublicKey);
        AppendImportingAccount(sb, NotesAccountName);
        AppendImportingAccount(sb, TasksAccountName);
        AppendImportingAccount(sb, AnonAccountName);
        foreach (var extra in extraTargetAccounts ?? [])
        {
            AppendImportingAccount(sb, extra);
        }

        sb.AppendLine("  SYS: {}");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("system_account: SYS");
        sb.AppendLine();

        AppendAuthorization(sb, issuerPublicKey, serviceUserPublicKey, xkeyPublicKey, calloutTimeout);
        return sb.ToString();
    }

    private static string Inv(ref DefaultInterpolatedStringHandler handler) =>
        string.Create(CultureInfo.InvariantCulture, ref handler);

    /// <summary>
    /// Appends the <c>AUTH</c> account block: the callout's own service-user NKey and the
    /// <c>$JS.API.&gt;</c>/<c>$KV.&gt;</c> exports every target account imports (see the type
    /// doc comment).
    /// </summary>
    private static void AppendAuthAccount(StringBuilder sb, string serviceUserPublicKey)
    {
        sb.AppendLine(Inv($"  {AuthAccountName}: {{"));
        sb.AppendLine("    jetstream: enabled");
        sb.AppendLine(Inv($"    users: [ {{ nkey: {serviceUserPublicKey} }} ]"));
        sb.AppendLine("    exports: [");
        sb.AppendLine("      { service: \"$JS.API.>\" }");
        sb.AppendLine("      { stream: \"$KV.>\" }");
        sb.AppendLine("    ]");
        sb.AppendLine("  }");
    }

    /// <summary>Appends the <c>authorization { auth_callout { ... } }</c> block.</summary>
    private static void AppendAuthorization(
        StringBuilder sb, string issuerPublicKey, string serviceUserPublicKey, string? xkeyPublicKey, TimeSpan? calloutTimeout)
    {
        sb.AppendLine("authorization {");
        if (calloutTimeout is { } timeout)
        {
            sb.AppendLine(Inv($"  timeout: \"{timeout.TotalSeconds}s\""));
        }

        sb.AppendLine("  auth_callout {");
        sb.AppendLine(Inv($"    issuer: {issuerPublicKey}"));
        sb.AppendLine(Inv($"    account: {AuthAccountName}"));
        sb.AppendLine(Inv($"    auth_users: [ {serviceUserPublicKey} ]"));
        if (xkeyPublicKey is not null)
        {
            sb.AppendLine(Inv($"    xkey: {xkeyPublicKey}"));
        }

        sb.AppendLine("  }");
        sb.AppendLine("}");
    }

    /// <summary>
    /// Appends a target account that imports <see cref="AuthAccountName"/>'s <c>$JS.API.&gt;</c>
    /// service and <c>$KV.&gt;</c> stream exports, rather than declaring its own local
    /// <c>jetstream: enabled</c>. A target account's own local JetStream domain would shadow
    /// the <c>$JS.API.&gt;</c> import (a KV lookup resolves against the account's own local
    /// JetStream if one exists, never reaching AUTH's), silently breaking every cross-account
    /// KV read with "stream not found" — none of these accounts have any JetStream assets of
    /// their own (the sample subjects are plain pub/sub, doc 08), so there is nothing to lose
    /// by relying entirely on the AUTH import.
    /// </summary>
    private static void AppendImportingAccount(StringBuilder sb, string accountName)
    {
        sb.AppendLine(Inv($"  {accountName}: {{"));
        sb.AppendLine("    imports: [");
        sb.AppendLine(Inv($"      {{ service: {{ account: {AuthAccountName}, subject: \"$JS.API.>\" }} }}"));
        sb.AppendLine(Inv($"      {{ stream: {{ account: {AuthAccountName}, subject: \"$KV.>\" }} }}"));
        sb.AppendLine("    ]");
        sb.AppendLine("  }");
    }
}
