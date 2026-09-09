// -----------------------------------------------------------------------
// <copyright file="TransformPipeline.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using AuthCallout.Contracts.Config;

namespace AuthCallout.Core.Templates;

/// <summary>
/// Executes a <see cref="TemplateVariable"/>'s ordered transform pipeline against one claim
/// value (ADR 0009 §Transform pipeline). Parsing the transform-spec grammar (JSON) is
/// <c>AuthCallout.Contracts</c>' <see cref="Transform.Parse"/>; this type only executes the
/// already-parsed, already-validated steps.
/// </summary>
/// <remarks>
/// Exact, pinned semantics per step (interop contract — do not change without an ADR update):
/// <list type="bullet">
/// <item><description><c>lowercase</c> / <c>uppercase</c>: invariant-culture case conversion.</description></item>
/// <item><description><c>replace(from -&gt; to)</c>: every ordinal occurrence of <see cref="Transform.From"/> replaced with <see cref="Transform.To"/>.</description></item>
/// <item><description><c>sanitize</c>: every character outside the safe set <c>[A-Za-z0-9_-]</c>
/// (which already covers NATS-special <c>.</c>/<c>*</c>/<c>&gt;</c> and whitespace) is replaced with <c>_</c>.</description></item>
/// <item><description><c>hash</c>: SHA-256 over the UTF-8 bytes of the value, rendered as lower-case hex, truncated to the first 16 characters. Stable across restarts (no per-process salt).</description></item>
/// <item><description><c>prefix(x)</c> / <c>suffix(x)</c>: prepend/append <see cref="Transform.Text"/> verbatim (not itself required to be safe until/unless followed by <c>require-safe</c>).</description></item>
/// <item><description><c>require-safe</c>: the pipeline fails (see <see cref="TryApply"/>'s return value) unless the value at this point is a non-empty string composed entirely of the safe set <c>[A-Za-z0-9_-]</c> (a single valid NATS subject token).</description></item>
/// </list>
/// Steps apply left to right; a value produced by one step is the input to the next.
/// </remarks>
public static class TransformPipeline
{
    private const string SafeCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-";

    /// <summary>The number of leading hex characters of the SHA-256 digest kept by the <c>hash</c> transform (ADR 0009).</summary>
    private const int HashLength = 16;

    /// <summary>
    /// Applies <paramref name="transforms"/>, in order, to <paramref name="value"/>.
    /// </summary>
    /// <param name="transforms">The ordered transform steps to apply.</param>
    /// <param name="value">The raw claim value to transform.</param>
    /// <param name="result">
    /// The transformed value. Populated with the value as of the failing <c>require-safe</c>
    /// step when this returns false, and with the final transformed value when it returns true.
    /// </param>
    /// <returns>False when a <c>require-safe</c> step rejects the value (ADR 0009's "otherwise the value is rejected and the connection denied"); true otherwise.</returns>
    public static bool TryApply(IReadOnlyList<Transform> transforms, string value, out string result)
    {
        ArgumentNullException.ThrowIfNull(transforms);
        ArgumentNullException.ThrowIfNull(value);

        var current = value;
        foreach (var transform in transforms)
        {
            if (transform.Kind == TransformKind.RequireSafe)
            {
                if (!IsSafeSubjectToken(current))
                {
                    result = current;
                    return false;
                }

                continue;
            }

            current = ApplyOne(transform, current);
        }

        result = current;
        return true;
    }

    /// <summary>True when <paramref name="value"/> is a non-empty string composed entirely of the safe set <c>[A-Za-z0-9_-]</c> (a single valid NATS subject token, ADR 0009).</summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True when the value is safe to embed directly in a subject.</returns>
    public static bool IsSafeSubjectToken(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length > 0 && value.All(static c => SafeCharacters.Contains(c, StringComparison.Ordinal));
    }

    private static string ApplyOne(Transform transform, string value) => transform.Kind switch
    {
#pragma warning disable S4040 // The lowercase/uppercase transforms are the ADR 0009 *output value* itself (e.g. producing a lowercase subject token), not a case-insensitive comparison normalization S4040 targets.
        TransformKind.Lowercase => value.ToLowerInvariant(),
#pragma warning restore S4040
        TransformKind.Uppercase => value.ToUpperInvariant(),
        TransformKind.Replace => value.Replace(transform.From ?? string.Empty, transform.To ?? string.Empty, StringComparison.Ordinal),
        TransformKind.Sanitize => Sanitize(value),
        TransformKind.Hash => Hash(value),
        TransformKind.Prefix => (transform.Text ?? string.Empty) + value,
        TransformKind.Suffix => value + (transform.Text ?? string.Empty),
        _ => value,
    };

    private static string Sanitize(string value)
    {
        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            buffer[i] = SafeCharacters.Contains(c, StringComparison.Ordinal) ? c : '_';
        }

        return new string(buffer);
    }

    private static string Hash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(digest)[..HashLength];
    }
}
