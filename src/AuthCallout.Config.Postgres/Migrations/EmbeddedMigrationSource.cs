// -----------------------------------------------------------------------
// <copyright file="EmbeddedMigrationSource.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Reflection;

namespace AuthCallout.Config.Postgres.Migrations;

/// <summary>
/// Loads the ordered <c>.sql</c> migration scripts embedded from <c>db/migrations/</c> as
/// assembly resources, keyed by filename and sorted lexically (the numeric filename prefix,
/// e.g. <c>0000_</c>/<c>0001_</c>, pins order).
/// </summary>
public sealed class EmbeddedMigrationSource
{
    private const string ResourcePrefix = "AuthCallout.Config.Postgres.Migrations.Sql.";

    /// <summary>Initializes a new instance of the <see cref="EmbeddedMigrationSource"/> class from a resource assembly.</summary>
    /// <param name="assembly">The assembly whose embedded <c>.sql</c> resources under the migrations folder to load.</param>
    public EmbeddedMigrationSource(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        OrderedMigrations = LoadFrom(assembly);
    }

    /// <summary>Gets the bundled default source, loading from this assembly.</summary>
    /// <value>The default embedded migration source.</value>
    public static EmbeddedMigrationSource Default { get; } = new(typeof(EmbeddedMigrationSource).Assembly);

    /// <summary>Gets every migration, in filename order.</summary>
    /// <value>The ordered migrations.</value>
    public IReadOnlyList<EmbeddedMigration> OrderedMigrations { get; }

    private static IReadOnlyList<EmbeddedMigration> LoadFrom(Assembly assembly)
    {
        var migrations = new List<EmbeddedMigration>();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !resourceName.EndsWith(".sql", StringComparison.Ordinal))
            {
                continue;
            }

            var filename = resourceName[ResourcePrefix.Length..];
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded migration resource '{resourceName}' could not be opened.");
            using var reader = new StreamReader(stream);
            migrations.Add(new EmbeddedMigration(filename, reader.ReadToEnd()));
        }

        migrations.Sort(static (a, b) => string.CompareOrdinal(a.Filename, b.Filename));
        return migrations;
    }
}
