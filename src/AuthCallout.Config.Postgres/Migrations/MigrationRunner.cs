// -----------------------------------------------------------------------
// <copyright file="MigrationRunner.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Npgsql;

namespace AuthCallout.Config.Postgres.Migrations;

/// <summary>
/// Applies the ordered <c>.sql</c> migration scripts embedded from <c>db/migrations/</c>
/// (P12 packet: "a tiny migration runner, no heavyweight framework"). Idempotent: each
/// migration's filename is recorded in <c>schema_migrations</c> after it runs, and a filename
/// already present there is skipped on a subsequent call — running the runner twice (or
/// concurrently from two instances racing the primary-key insert) is safe.
/// </summary>
public sealed class MigrationRunner
{
    private const string SchemaMigrationsTableSql = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
          filename     text PRIMARY KEY,
          applied_at   timestamptz NOT NULL DEFAULT now()
        );
        """;

    private readonly EmbeddedMigrationSource _migrations;

    /// <summary>Initializes a new instance of the <see cref="MigrationRunner"/> class using the bundled embedded migrations.</summary>
    public MigrationRunner()
        : this(EmbeddedMigrationSource.Default)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MigrationRunner"/> class over a caller-supplied migration source (tests).</summary>
    /// <param name="migrations">The ordered migration source to apply.</param>
    public MigrationRunner(EmbeddedMigrationSource migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        _migrations = migrations;
    }

    /// <summary>
    /// Applies every migration not yet recorded in <c>schema_migrations</c>, in filename order,
    /// each in its own transaction.
    /// </summary>
    /// <param name="connection">An open connection to run migrations against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The filenames of migrations actually applied by this call (empty if all were already applied).</returns>
    public async Task<IReadOnlyList<string>> RunAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await EnsureMigrationsTableAsync(connection, cancellationToken);
        var alreadyApplied = await LoadAppliedFilenamesAsync(connection, cancellationToken);

        var applied = new List<string>();
        foreach (var migration in _migrations.OrderedMigrations)
        {
            if (alreadyApplied.Contains(migration.Filename))
            {
                continue;
            }

            await ApplyOneAsync(connection, migration, cancellationToken);
            applied.Add(migration.Filename);
        }

        return applied;
    }

    private static async Task EnsureMigrationsTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SchemaMigrationsTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> LoadAppliedFilenamesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand("SELECT filename FROM schema_migrations;", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(reader.GetString(0));
        }

        return applied;
    }

    private static async Task ApplyOneAsync(NpgsqlConnection connection, EmbeddedMigration migration, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // migration.Sql is a build-time-embedded resource under this packet's own control
            // (db/migrations/*.sql), never external/user input - CA2100 flags any non-parameterized
            // NpgsqlCommand text on principle, but there is nothing to parameterize here: the whole
            // point of a migration script is to run arbitrary DDL/DML from a trusted source.
#pragma warning disable CA2100
            await using (var migrationCommand = new NpgsqlCommand(migration.Sql, connection, transaction))
#pragma warning restore CA2100
            {
                await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var recordCommand = new NpgsqlCommand(
                "INSERT INTO schema_migrations (filename) VALUES (@filename) ON CONFLICT (filename) DO NOTHING;", connection, transaction))
            {
                recordCommand.Parameters.AddWithValue("filename", migration.Filename);
                await recordCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
