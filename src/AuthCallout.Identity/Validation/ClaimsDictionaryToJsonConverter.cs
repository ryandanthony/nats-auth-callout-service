// -----------------------------------------------------------------------
// <copyright file="ClaimsDictionaryToJsonConverter.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections;
using System.Text.Json;

namespace AuthCallout.Identity.Validation;

/// <summary>
/// Converts <see cref="Microsoft.IdentityModel.Tokens.TokenValidationResult.Claims"/> (an
/// <see cref="IDictionary{TKey,TValue}"/> of <see cref="string"/> to <see cref="object"/>) into a
/// <see cref="JsonElement"/> object, for <see cref="TokenValidator"/> to hand to
/// <see cref="AuthCallout.Core.Claims.ClaimsAccessor"/>. Writes directly with
/// <see cref="Utf8JsonWriter"/> rather than going through <c>JsonSerializer.Serialize&lt;object&gt;</c>
/// (which the caller previously did): serializing a boxed <c>object</c> is reflection-based —
/// <c>JsonSerializer</c> has to inspect the runtime type of every value at serialization time —
/// which trips <c>IL2026</c> (<c>RequiresUnreferencedCode</c>) under <c>PublishTrimmed</c> (doc 08
/// §Runtime &amp; trimming; this type exists to keep the Dockerfile's trimmed, R2R publish
/// working). The value shapes Microsoft.IdentityModel actually produces are a small, fixed set
/// (empirically confirmed by decoding a representative token through
/// <c>JwtSecurityTokenHandler.ValidateTokenAsync</c>): <see cref="string"/>, <see cref="long"/>,
/// <see cref="double"/>, <see cref="bool"/>, <see cref="IEnumerable"/> (JSON arrays, e.g.
/// <c>List&lt;object&gt;</c>), and <see cref="JsonElement"/> itself for claims the library already
/// parsed as nested JSON (e.g. <c>realm_access</c>) — so an explicit type-pattern switch covers
/// every case with no reflection, unlike a general-purpose serializer. Split into a short
/// dispatch method plus one handler per shape category (rather than one long switch) to stay
/// under this repo's method-length analyzer limit.
/// </summary>
internal static class ClaimsDictionaryToJsonConverter
{
    /// <summary>Converts a claims dictionary into a parsed <see cref="JsonElement"/> object.</summary>
    /// <param name="claims">The claim set, keyed by claim name.</param>
    /// <returns>The equivalent JSON object element, owning its own backing document (safe to outlive this call).</returns>
    internal static JsonElement Convert(IDictionary<string, object> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteObject(writer, claims);
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteObject(Utf8JsonWriter writer, IDictionary<string, object> obj)
    {
        writer.WriteStartObject();
        foreach (var (key, value) in obj)
        {
            writer.WritePropertyName(key);
            WriteValue(writer, value);
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                {
                    writer.WriteNullValue();
                    break;
                }

            case JsonElement element:
                {
                    element.WriteTo(writer);
                    break;
                }

            case IDictionary<string, object> nested:
                {
                    WriteObject(writer, nested);
                    break;
                }

            // string must be matched before IEnumerable: string is IEnumerable<char>, so
            // without this a scalar string claim (e.g. azp, preferred_username) would fall
            // into the IEnumerable arm below and be written as a JSON array of single-character
            // strings, breaking every downstream claim match. Route it to WriteScalar instead.
            case string:
                {
                    WriteScalar(writer, value);
                    break;
                }

            case IEnumerable enumerable:
                {
                    WriteArray(writer, enumerable);
                    break;
                }

            default:
                {
                    WriteScalar(writer, value);
                    break;
                }
        }
    }

    private static void WriteArray(Utf8JsonWriter writer, IEnumerable enumerable)
    {
        writer.WriteStartArray();
        foreach (var item in enumerable)
        {
            WriteValue(writer, item);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes every non-null, non-<see cref="JsonElement"/>, non-collection boxed value shape
    /// Microsoft.IdentityModel is observed to produce (see this type's doc comment). The final
    /// default case is a defensive fallback for any other boxed type: writes its string form
    /// rather than throwing, so a validated token can never fail downstream claim access over a
    /// shape this converter didn't anticipate.
    /// </summary>
    private static void WriteScalar(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case string s:
                {
                    writer.WriteStringValue(s);
                    break;
                }

            case bool b:
                {
                    writer.WriteBooleanValue(b);
                    break;
                }

            case DateTime dt:
                {
                    writer.WriteStringValue(dt);
                    break;
                }

            case DateTimeOffset dto:
                {
                    writer.WriteStringValue(dto);
                    break;
                }

            case int or long or double:
                {
                    WriteNumber(writer, value);
                    break;
                }

            case float or decimal:
                {
                    WriteNumber(writer, value);
                    break;
                }

            default:
                {
                    writer.WriteStringValue(value.ToString());
                    break;
                }
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case int i:
                {
                    writer.WriteNumberValue(i);
                    break;
                }

            case long l:
                {
                    writer.WriteNumberValue(l);
                    break;
                }

            case double d:
                {
                    writer.WriteNumberValue(d);
                    break;
                }

            case float f:
                {
                    writer.WriteNumberValue(f);
                    break;
                }

            case decimal m:
                {
                    writer.WriteNumberValue(m);
                    break;
                }

            default:
                {
                    // Unreachable: WriteScalar only ever routes int/long/double/float/decimal
                    // here (see the two case-patterns above it). No boxed value of any other
                    // type reaches this method.
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Unrecognized numeric type; expected int, long, double, float, or decimal.");
                }
        }
    }
}
