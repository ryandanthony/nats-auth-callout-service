// -----------------------------------------------------------------------
// <copyright file="PostgresAuthApplicationRepository.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Text.Json;
using AuthCallout.Config.Postgres.Migrations;
using Dapper;
using Npgsql;

namespace AuthCallout.Config.Postgres.Repository;

/// <summary>
/// The real <see cref="IAuthApplicationRepository"/>, backed by Npgsql + Dapper (P12 packet).
/// Owns its own dedicated connection for <see cref="ListenForChangesAsync"/> (a Postgres
/// <c>LISTEN</c> session must stay open on one connection for its lifetime) and opens a fresh,
/// short-lived connection per <see cref="ListEnabledAsync"/> call.
/// </summary>
public sealed class PostgresAuthApplicationRepository : IAuthApplicationRepository
{
    private readonly string _connectionString;
    private readonly string _notifyChannel;
    private readonly MigrationRunner _migrationRunner;

    /// <summary>Initializes a new instance of the <see cref="PostgresAuthApplicationRepository"/> class.</summary>
    /// <param name="connectionString">The resolved PostgreSQL connection string (plaintext; the caller already resolved the <c>SecretRef</c>).</param>
    /// <param name="notifyChannel">The LISTEN/NOTIFY channel name (<see cref="AuthCallout.Contracts.Config.PostgresSourceSettings.NotifyChannel"/>).</param>
    public PostgresAuthApplicationRepository(string connectionString, string notifyChannel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(notifyChannel);
        _connectionString = connectionString;
        _notifyChannel = notifyChannel;
        _migrationRunner = new MigrationRunner();
    }

    /// <summary>
    /// Applies any pending migrations (db/migrations/*.sql) against the configured database.
    /// Idempotent; safe to call every time the provider starts (P12 packet: optional
    /// <c>migrateOnStart</c> setting at the host level — this method is what that setting calls).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The filenames of migrations actually applied by this call.</returns>
    public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await _migrationRunner.RunAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuthApplicationRow>> ListEnabledAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var command = new CommandDefinition(
            "SELECT name, spec, enabled FROM auth_applications WHERE enabled = true ORDER BY name;",
            cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<RawRow>(command).ConfigureAwait(false);

        var result = new List<AuthApplicationRow>();
        foreach (var row in rows)
        {
            using var doc = JsonDocument.Parse(row.Spec);
            result.Add(new AuthApplicationRow(row.Name, doc.RootElement.Clone(), row.Enabled));
        }

        return result;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<NotificationSignal> ListenForChangesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // LISTEN's channel name cannot be a bind parameter (it is DDL-like session state, not
        // query data); _notifyChannel comes only from this service's own bootstrap config
        // (PostgresSourceSettings.NotifyChannel), never external/user input, and is quoted as an
        // identifier above to guard against embedded quote characters.
#pragma warning disable CA2100
        await using (var listenCommand = new NpgsqlCommand($"LISTEN {QuoteIdentifier(_notifyChannel)};", connection))
#pragma warning restore CA2100
        {
            await listenCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // NpgsqlConnection.WaitAsync blocks until at least one notification arrives (firing
        // the Notification event synchronously first) or the token is cancelled; it does not
        // itself hand back the payload, so this loop's only job is "wake up, tell the caller
        // something changed" - the provider always re-lists rather than trusting any payload
        // (NotificationSignal is deliberately content-free).
        while (true)
        {
            try
            {
                await connection.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            yield return NotificationSignal.Instance;
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Quotes a Postgres identifier for use directly in DDL/LISTEN text (channel names cannot
    /// be parameterized in <c>LISTEN</c>; <paramref name="identifier"/> comes only from this
    /// service's own bootstrap config, never external input).
    /// </summary>
    private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    /// <summary>The raw Dapper row shape before JSON parsing.</summary>
    private sealed record RawRow(string Name, string Spec, bool Enabled);
}
