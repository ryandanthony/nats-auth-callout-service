// -----------------------------------------------------------------------
// <copyright file="AuthOutcomeEventJsonContext.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace AuthCallout.Nats.Events;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for <see cref="AuthOutcomeEventPayload"/>
/// (trim/R2R-safe: no reflection at serialize time, doc 08 §Runtime &amp; trimming; keeps our own
/// code trim-clean even though the Dockerfile currently publishes ReadyToRun without trimming). Every naming/ignore-condition/
/// converter setting must be on this generator attribute itself, not on a runtime
/// <see cref="System.Text.Json.JsonSerializerOptions"/> instance built around
/// <see cref="Default"/>: wrapping a source-generated context in
/// <c>new JsonSerializerOptions { TypeInfoResolver = ... }</c> and then calling
/// <c>options.GetTypeInfo(type)</c> silently drops <see cref="JsonSourceGenerationOptionsAttribute.PropertyNamingPolicy"/>
/// (empirically confirmed: <c>options.GetTypeInfo</c> falls back to the CLR property name), so
/// callers must use <see cref="Default"/>'s own generated <c>AuthOutcomeEventPayload</c>
/// <see cref="Metadata.JsonTypeInfo{T}"/> property directly (e.g.
/// <c>JsonSerializer.SerializeToUtf8Bytes(payload, AuthOutcomeEventJsonContext.Default.AuthOutcomeEventPayload)</c>).
/// <see cref="Rfc3339DateTimeOffsetConverter"/> is listed in
/// <see cref="JsonSourceGenerationOptionsAttribute.Converters"/> for the same reason: a converter
/// added to a separate runtime <c>JsonSerializerOptions.Converters</c> list would never be
/// consulted by the generated type info the call site actually uses.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(Rfc3339DateTimeOffsetConverter)])]
[JsonSerializable(typeof(AuthOutcomeEventPayload))]
internal sealed partial class AuthOutcomeEventJsonContext : JsonSerializerContext;
