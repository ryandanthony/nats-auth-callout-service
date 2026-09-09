// -----------------------------------------------------------------------
// <copyright file="EmbeddedMigration.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Config.Postgres.Migrations;

/// <summary>One ordered migration: its filename (the <c>schema_migrations</c> key) and SQL body.</summary>
/// <param name="Filename">The migration's filename, e.g. <c>0001_create_auth_applications.sql</c>.</param>
/// <param name="Sql">The migration's SQL body.</param>
public sealed record EmbeddedMigration(string Filename, string Sql);
