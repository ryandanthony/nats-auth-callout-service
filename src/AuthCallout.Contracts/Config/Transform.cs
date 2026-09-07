// -----------------------------------------------------------------------
// <copyright file="Transform.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// One parsed step of a <see cref="TemplateVariable"/>'s transform pipeline (ADR 0009).
/// This is the **parsed representation only** — the transform-spec grammar's parser lives
/// in <c>AuthCallout.Contracts</c> (config validation needs to know every transform is
/// known and well-formed at load time), while *execution* of the pipeline against a claim
/// value lives in <c>AuthCallout.Core</c> (P04's <c>Templates</c> engine), per the P02
/// packet's split. Immutable, value-equal (record with only scalar members).
/// </summary>
/// <param name="Kind">Which transform step this is.</param>
/// <param name="From">The substring to replace. Set only for <see cref="TransformKind.Replace"/>.</param>
/// <param name="To">The replacement substring. Set only for <see cref="TransformKind.Replace"/>.</param>
/// <param name="Text">The text to prepend/append. Set only for <see cref="TransformKind.Prefix"/>/<see cref="TransformKind.Suffix"/>.</param>
public sealed record Transform(TransformKind Kind, string? From = null, string? To = null, string? Text = null)
{
    /// <summary>Gets the <c>lowercase</c> transform.</summary>
    /// <value>The <c>lowercase</c> transform.</value>
    public static Transform Lowercase { get; } = new(TransformKind.Lowercase);

    /// <summary>Gets the <c>uppercase</c> transform.</summary>
    /// <value>The <c>uppercase</c> transform.</value>
    public static Transform Uppercase { get; } = new(TransformKind.Uppercase);

    /// <summary>Gets the <c>sanitize</c> transform.</summary>
    /// <value>The <c>sanitize</c> transform.</value>
    public static Transform Sanitize { get; } = new(TransformKind.Sanitize);

    /// <summary>Gets the <c>hash</c> transform.</summary>
    /// <value>The <c>hash</c> transform.</value>
    public static Transform Hash { get; } = new(TransformKind.Hash);

    /// <summary>Gets the <c>require-safe</c> transform.</summary>
    /// <value>The <c>require-safe</c> transform.</value>
    public static Transform RequireSafe { get; } = new(TransformKind.RequireSafe);

    /// <summary>Creates a <c>replace(from -&gt; to)</c> transform.</summary>
    /// <param name="from">The substring to replace.</param>
    /// <param name="to">The replacement substring.</param>
    /// <returns>The constructed <c>replace</c> transform.</returns>
    public static Transform ReplaceOf(string from, string to)
    {
        ArgumentException.ThrowIfNullOrEmpty(from);
        ArgumentNullException.ThrowIfNull(to);
        return new Transform(TransformKind.Replace, From: from, To: to);
    }

    /// <summary>Creates a <c>prefix(text)</c> transform.</summary>
    /// <param name="text">The text to prepend.</param>
    /// <returns>The constructed <c>prefix</c> transform.</returns>
    public static Transform PrefixOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Transform(TransformKind.Prefix, Text: text);
    }

    /// <summary>Creates a <c>suffix(text)</c> transform.</summary>
    /// <param name="text">The text to append.</param>
    /// <returns>The constructed <c>suffix</c> transform.</returns>
    public static Transform SuffixOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Transform(TransformKind.Suffix, Text: text);
    }

    /// <summary>
    /// Parses one transform-spec entry from its JSON representation (doc 09's file/CRD
    /// shape). Two grammars, matching the ADR 0009 example
    /// (<c>[ "lowercase", { "replace": { "from": ".", "to": "_" } } ]</c>):
    /// <list type="bullet">
    /// <item><description>A bare JSON string naming a no-argument transform: <c>lowercase</c>, <c>uppercase</c>, <c>sanitize</c>, <c>hash</c>, <c>require-safe</c>.</description></item>
    /// <item><description>A single-property JSON object naming an argument-taking transform: <c>{ "replace": { "from": "...", "to": "..." } }</c>, <c>{ "prefix": "..." }</c>, <c>{ "suffix": "..." }</c>.</description></item>
    /// </list>
    /// </summary>
    /// <param name="raw">The JSON element for one transform-list entry.</param>
    /// <returns>The parsed transform.</returns>
    /// <exception cref="FormatException">The entry does not match either grammar, names an unknown transform, or is missing a required argument.</exception>
    public static Transform Parse(JsonElement raw)
    {
        if (!TryParse(raw, out var transform, out var error))
        {
            throw new FormatException(error);
        }

        return transform;
    }

    /// <summary>Attempts to parse a transform entry. See <see cref="Parse"/> for the grammar.</summary>
    /// <param name="raw">The JSON element for one transform-list entry.</param>
    /// <param name="transform">The parsed transform, when this returns true.</param>
    /// <param name="error">A description of why parsing failed, when this returns false.</param>
    /// <returns>True when parsing succeeded and <paramref name="transform"/> is populated; false when <paramref name="error"/> describes why.</returns>
#pragma warning disable CA1021 // Two 'out' params mirror the .NET TryParse pattern generalized with a diagnostic message.
    public static bool TryParse(JsonElement raw, out Transform transform, out string? error)
#pragma warning restore CA1021
    {
        transform = Lowercase;
        error = null;

        if (raw.ValueKind == JsonValueKind.String)
        {
            return TryParseNoArgTransform(raw.GetString(), out transform, out error);
        }

        if (raw.ValueKind == JsonValueKind.Object)
        {
            return TryParseArgTransform(raw, out transform, out error);
        }

        error = $"A transform entry must be a string or a single-property object, not {raw.ValueKind}.";
        return false;
    }

#pragma warning disable CA1021
    private static bool TryParseNoArgTransform(string? name, out Transform transform, out string? error)
#pragma warning restore CA1021
    {
        switch (name)
        {
            case "lowercase":
                {
                    transform = Lowercase;
                    error = null;
                    return true;
                }

            case "uppercase":
                {
                    transform = Uppercase;
                    error = null;
                    return true;
                }

            case "sanitize":
                {
                    transform = Sanitize;
                    error = null;
                    return true;
                }

            case "hash":
                {
                    transform = Hash;
                    error = null;
                    return true;
                }

            case "require-safe":
                {
                    transform = RequireSafe;
                    error = null;
                    return true;
                }

            default:
                {
                    transform = Lowercase;
                    error = $"Unknown transform '{name}'. Known no-argument transforms: lowercase, uppercase, sanitize, hash, require-safe.";
                    return false;
                }
        }
    }

#pragma warning disable CA1021
    private static bool TryParseArgTransform(JsonElement raw, out Transform transform, out string? error)
#pragma warning restore CA1021
    {
        transform = Lowercase;
        var properties = raw.EnumerateObject().ToArray();
        if (properties.Length != 1)
        {
            error = "A transform object must have exactly one property naming the transform (e.g. { \"replace\": { \"from\": \".\", \"to\": \"_\" } }).";
            return false;
        }

        var property = properties[0];
        switch (property.Name)
        {
            case "replace":
                {
                    return TryParseReplace(property.Value, out transform, out error);
                }

            case "prefix":
                {
                    return TryParseTextArg(property.Value, TransformKind.Prefix, out transform, out error);
                }

            case "suffix":
                {
                    return TryParseTextArg(property.Value, TransformKind.Suffix, out transform, out error);
                }

            default:
                {
                    error = $"Unknown transform '{property.Name}'. Known argument-taking transforms: replace, prefix, suffix.";
                    return false;
                }
        }
    }

#pragma warning disable CA1021
    private static bool TryParseReplace(JsonElement args, out Transform transform, out string? error)
#pragma warning restore CA1021
    {
        transform = Lowercase;
        if (args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty("from", out var fromEl)
            || fromEl.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(fromEl.GetString()))
        {
            error = "'replace' requires an object argument with a non-empty string 'from' (e.g. { \"replace\": { \"from\": \".\", \"to\": \"_\" } }).";
            return false;
        }

        if (!args.TryGetProperty("to", out var toEl) || toEl.ValueKind != JsonValueKind.String)
        {
            error = "'replace' requires a string 'to' property (may be empty, to delete occurrences of 'from').";
            return false;
        }

        transform = ReplaceOf(fromEl.GetString()!, toEl.GetString()!);
        error = null;
        return true;
    }

    private static bool TryParseTextArg(JsonElement arg, TransformKind kind, out Transform transform, out string? error)
    {
        transform = Lowercase;
        if (arg.ValueKind != JsonValueKind.String)
        {
            error = $"'{(kind == TransformKind.Prefix ? "prefix" : "suffix")}' requires a string argument.";
            return false;
        }

        transform = kind == TransformKind.Prefix ? PrefixOf(arg.GetString()!) : SuffixOf(arg.GetString()!);
        error = null;
        return true;
    }
}
