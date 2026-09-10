// -----------------------------------------------------------------------
// <copyright file="Rfc3339DateTimeOffsetConverter.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthCallout.Nats.Events;

/// <summary>
/// Serializes a <see cref="DateTimeOffset"/> as an RFC 3339 UTC <c>Z</c>-suffixed string
/// (doc 09 §1: <c>"timestamp": "2026-09-06T22:00:00Z"</c>), rather than
/// <see cref="System.Text.Json"/>'s default <c>+00:00</c>-suffixed round-trip format. Both are
/// valid RFC 3339, but the doc-09 example and every fixture this packet ships use the
/// <c>Z</c> form, so this converter keeps emitted payloads byte-identical to the documented
/// shape rather than merely schema-format-valid.
/// </summary>
internal sealed class Rfc3339DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    /// <summary>
    /// The exact doc-09 timestamp format: second precision, <c>Z</c>-suffixed UTC (e.g.
    /// <c>2026-09-06T22:00:00Z</c>). Named rather than inlined per SonarAnalyzer S6585
    /// ("do not hardcode the format specifier").
    /// </summary>
    private const string Rfc3339SecondsFormat = "yyyy-MM-ddTHH:mm:ssZ";

    /// <inheritdoc />
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
#pragma warning disable S6585 // The built-in "u" standard specifier is close but uses a space instead of 'T' (e.g. "2026-09-06 22:00:00Z"); doc 09's exact example ("2026-09-06T22:00:00Z") requires the custom pattern.
        writer.WriteStringValue(value.UtcDateTime.ToString(Rfc3339SecondsFormat, System.Globalization.CultureInfo.InvariantCulture));
#pragma warning restore S6585
    }
}
