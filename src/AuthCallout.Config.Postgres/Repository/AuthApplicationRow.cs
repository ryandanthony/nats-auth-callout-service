// -----------------------------------------------------------------------
// <copyright file="AuthApplicationRow.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;

namespace AuthCallout.Config.Postgres.Repository;

/// <summary>One row of the <c>auth_applications</c> table (db/migrations/0001, P12).</summary>
/// <param name="Name">The row's primary key, expected to match the JSONB <c>spec</c>'s own <c>name</c> field.</param>
/// <param name="Spec">The application document (doc 09 §3 file schema v1 shape), mapped by the shared <c>ApplicationFileMapper</c> (ADR 0007).</param>
/// <param name="Enabled">Whether this row participates in the aggregate. A disabled row is excluded before mapping (P12 ADR→test map).</param>
public sealed record AuthApplicationRow(string Name, JsonElement Spec, bool Enabled);
