// -----------------------------------------------------------------------
// <copyright file="ClaimsAccessor.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Core.Claims;

/// <summary>
/// <see cref="IClaimsAccessor"/> over a validated token's claim set, represented as a JSON
/// object (<c>docs/design/02-authorization-model.md</c> §Claim match). Callers (e.g. the
/// token validator, P06) build the claim payload once per request and pass it in; this type
/// never talks to a token library directly, keeping <c>AuthCallout.Core</c> free of any
/// transport/identity package reference.
/// </summary>
/// <remarks>
/// <para><b>Nested paths</b> (e.g. <c>realm_access.roles</c>) are resolved by splitting the
/// path on <c>.</c> and descending through JSON object properties; a path segment that does
/// not resolve to an object property (including one hitting a JSON array or scalar before the
/// final segment) makes the whole path absent.</para>
/// <para><b>Coercion rules</b> (pinned, interop contract for claim consumers):</para>
/// <list type="bullet">
/// <item><description>A JSON string value yields that string.</description></item>
/// <item><description>A JSON number value yields its raw JSON text (already culture-invariant;
/// this avoids the floating-point round-trip artifacts <see cref="JsonElement.GetDouble"/>
/// could introduce, e.g. keeping <c>42</c> as <c>"42"</c> and <c>3.14</c> as <c>"3.14"</c>).</description></item>
/// <item><description>A JSON boolean value yields <c>"true"</c> or <c>"false"</c> (lower-case, matching JSON literal form).</description></item>
/// <item><description>A JSON array yields one value per element, applying these same scalar
/// coercion rules per element; an element that is itself an object, array, or <c>null</c>
/// contributes no value (skipped, not an error).</description></item>
/// <item><description>A JSON object (at the final path segment) contributes no value: objects are never implicitly stringified.</description></item>
/// <item><description>A JSON <c>null</c> value, or an absent path, contributes no value.</description></item>
/// </list>
/// </remarks>
public sealed class ClaimsAccessor : IClaimsAccessor
{
    private readonly JsonElement _root;

    /// <summary>Initializes a new instance of the <see cref="ClaimsAccessor"/> class over a parsed claims JSON object.</summary>
    /// <param name="claims">The claim set as a JSON object element (e.g. a decoded JWT payload).</param>
    /// <exception cref="ArgumentException"><paramref name="claims"/> is not a JSON object.</exception>
    public ClaimsAccessor(JsonElement claims)
    {
        if (claims.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"Claims must be a JSON object, not {claims.ValueKind}.", nameof(claims));
        }

        _root = claims.Clone();
    }

    /// <summary>Builds a <see cref="ClaimsAccessor"/> by parsing a JSON object document (e.g. a decoded JWT payload).</summary>
    /// <param name="json">The claims JSON document text.</param>
    /// <returns>The constructed accessor.</returns>
    /// <exception cref="JsonException"><paramref name="json"/> is not valid JSON.</exception>
    /// <exception cref="ArgumentException">The parsed document's root is not a JSON object.</exception>
    public static ClaimsAccessor Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        return new ClaimsAccessor(document.RootElement);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetValues(string claimPath)
    {
        ArgumentNullException.ThrowIfNull(claimPath);

        var element = Navigate(claimPath);
        return element is null ? [] : ExtractValues(element.Value);
    }

    /// <inheritdoc />
    public string? GetSingleValue(string claimPath)
    {
        var values = GetValues(claimPath);
        return values.Count > 0 ? values[0] : null;
    }

    /// <inheritdoc />
    public bool HasClaim(string claimPath) => GetValues(claimPath).Count > 0;

    private JsonElement? Navigate(string claimPath)
    {
        var current = _root;
        foreach (var segment in claimPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static IReadOnlyList<string> ExtractValues(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return TryCoerceScalar(element, out var single) ? [single] : [];
        }

        var values = new List<string>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            if (TryCoerceScalar(item, out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static bool TryCoerceScalar(JsonElement element, out string value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                {
                    value = element.GetString()!;
                    return true;
                }

            case JsonValueKind.Number:
                {
                    value = element.GetRawText();
                    return true;
                }

            case JsonValueKind.True:
                {
                    value = "true";
                    return true;
                }

            case JsonValueKind.False:
                {
                    value = "false";
                    return true;
                }

            default:
                {
                    // Object, Array (nested), Null, Undefined: contributes no value.
                    value = string.Empty;
                    return false;
                }
        }
    }
}
