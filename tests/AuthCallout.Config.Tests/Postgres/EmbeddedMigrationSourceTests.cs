// -----------------------------------------------------------------------
// <copyright file="EmbeddedMigrationSourceTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Config.Postgres.Migrations;

namespace AuthCallout.Config.Tests.Postgres;

/// <summary>
/// Unit tests for <see cref="EmbeddedMigrationSource"/>: pure resource loading/ordering, no
/// database required (real apply-and-idempotency behavior is a P14 integration concern per the
/// packet's own testing split, since it needs a live Postgres to be meaningful).
/// </summary>
public sealed class EmbeddedMigrationSourceTests
{
    [Fact]
    public void Default_LoadsAllMigrationsFromDbMigrationsFolder()
    {
        var source = EmbeddedMigrationSource.Default;

        Assert.NotEmpty(source.OrderedMigrations);
        Assert.Contains(source.OrderedMigrations, static m => string.Equals(m.Filename, "0000_schema_migrations.sql", StringComparison.Ordinal));
        Assert.Contains(source.OrderedMigrations, static m => string.Equals(m.Filename, "0001_create_auth_applications.sql", StringComparison.Ordinal));
    }

    [Fact]
    public void OrderedMigrations_AreSortedByFilename()
    {
        var source = EmbeddedMigrationSource.Default;

        var filenames = source.OrderedMigrations.Select(static m => m.Filename).ToList();
        var sorted = filenames.OrderBy(static f => f, StringComparer.Ordinal).ToList();

        Assert.Equal(sorted, filenames);
    }

    [Fact]
    public void EachMigration_HasNonEmptySql()
    {
        var source = EmbeddedMigrationSource.Default;

        Assert.All(source.OrderedMigrations, static m => Assert.False(string.IsNullOrWhiteSpace(m.Sql)));
    }

    [Fact]
    public void CreateAuthApplicationsMigration_DefinesExpectedTableAndTrigger()
    {
        var source = EmbeddedMigrationSource.Default;

        var migration = source.OrderedMigrations.Single(
            static m => string.Equals(m.Filename, "0001_create_auth_applications.sql", StringComparison.Ordinal));

        Assert.Contains("auth_applications", migration.Sql, StringComparison.Ordinal);
        Assert.Contains("auth_applications_changed", migration.Sql, StringComparison.Ordinal);
    }
}
