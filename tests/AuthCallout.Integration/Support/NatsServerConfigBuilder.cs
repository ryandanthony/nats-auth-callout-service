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
/// <c>ANON</c>) is declared empty — the minted user JWT names the account, the server places
/// the connection there (doc 03 §Mint and respond) — plus the mandatory <c>SYS</c> system
/// account. JetStream is always enabled (doc 07/08: KV buckets require it).
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
        sb.AppendLine(Inv($"  {AuthAccountName}: {{"));
        sb.AppendLine("    jetstream: enabled");
        sb.AppendLine(Inv($"    users: [ {{ nkey: {serviceUserPublicKey} }} ]"));
        sb.AppendLine("  }");
        sb.AppendLine(Inv($"  {NotesAccountName}: {{ jetstream: enabled }}"));
        sb.AppendLine(Inv($"  {TasksAccountName}: {{ jetstream: enabled }}"));
        sb.AppendLine(Inv($"  {AnonAccountName}: {{ jetstream: enabled }}"));
        foreach (var extra in extraTargetAccounts ?? [])
        {
            sb.AppendLine(Inv($"  {extra}: {{ jetstream: enabled }}"));
        }

        sb.AppendLine("  SYS: {}");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("system_account: SYS");
        sb.AppendLine();

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

        return sb.ToString();
    }

    private static string Inv(ref DefaultInterpolatedStringHandler handler) =>
        string.Create(CultureInfo.InvariantCulture, ref handler);
}
