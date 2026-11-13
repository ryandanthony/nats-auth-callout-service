// -----------------------------------------------------------------------
// <copyright file="DurationString.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;

namespace AuthCallout.Contracts.Primitives;

/// <summary>
/// A duration expressed the way doc 04/09 write them in config: a non-negative integer
/// followed by a unit suffix (<c>ms</c>, <c>s</c>, <c>m</c>, <c>h</c>), e.g. <c>60s</c>,
/// <c>5m</c>, <c>1500ms</c>. Immutable, value-equal via the underlying <see cref="TimeSpan"/>.
/// Every duration-typed field in the canonical model (doc 04) is a <see cref="DurationString"/>
/// rather than a bare <see cref="TimeSpan"/>, so the exact wire representation and the parse
/// failure mode are pinned in one place.
/// </summary>
public readonly struct DurationString : IEquatable<DurationString>
{
    private DurationString(TimeSpan value, string text)
    {
        Value = value;
        Text = text;
    }

    /// <summary>Gets the parsed duration.</summary>
    /// <value>The parsed duration.</value>
    public TimeSpan Value { get; }

    /// <summary>Gets the original, canonical text form (unit-suffixed).</summary>
    /// <value>The canonical text form.</value>
    public string Text { get; }

    /// <summary>Compares two durations for equality.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when both durations are equal.</returns>
    public static bool operator ==(DurationString left, DurationString right) => left.Equals(right);

    /// <summary>Compares two durations for inequality.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when the durations differ.</returns>
    public static bool operator !=(DurationString left, DurationString right) => !left.Equals(right);

    /// <summary>Constructs a <see cref="DurationString"/> directly from a <see cref="TimeSpan"/>, formatting canonical text.</summary>
    /// <param name="value">The duration value.</param>
    /// <returns>The constructed duration string.</returns>
    public static DurationString FromTimeSpan(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Duration must not be negative.");
        }

        return new DurationString(value, Format(value));
    }

    /// <summary>
    /// Parses a duration string (<c>{nonNegativeInteger}{unit}</c>, unit one of <c>ms|s|m|h</c>).
    /// Throws <see cref="FormatException"/> on malformed input, per the config-load
    /// fail-closed rules (ADR 0006) — the aggregate validator (P02) turns this into a
    /// deterministic error string rather than letting the exception leak raw.
    /// </summary>
    /// <param name="text">The duration string to parse.</param>
    /// <returns>The parsed duration.</returns>
    public static DurationString Parse(string text)
    {
        if (!TryParse(text, out var result))
        {
            throw new FormatException($"'{text}' is not a valid duration string (expected e.g. '60s', '5m', '1500ms', '1h').");
        }

        return result;
    }

    /// <summary>Attempts to parse a duration string. See <see cref="Parse"/> for the accepted grammar.</summary>
    /// <param name="text">The duration string to parse.</param>
    /// <param name="result">The parsed duration, when this returns true.</param>
    /// <returns>True when parsing succeeded.</returns>
    public static bool TryParse(string? text, out DurationString result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.Trim().AsSpan();
        var digitsEnd = 0;
        while (digitsEnd < span.Length && char.IsAsciiDigit(span[digitsEnd]))
        {
            digitsEnd++;
        }

        if (digitsEnd == 0 || digitsEnd == span.Length)
        {
            return false;
        }

        var numberSpan = span[..digitsEnd];
        var unitSpan = span[digitsEnd..];

        if (!long.TryParse(numberSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var magnitude))
        {
            return false;
        }

        TimeSpan value;
        try
        {
            value = unitSpan switch
            {
                "ms" => TimeSpan.FromMilliseconds(magnitude),
                "s" => TimeSpan.FromSeconds(magnitude),
                "m" => TimeSpan.FromMinutes(magnitude),
                "h" => TimeSpan.FromHours(magnitude),
                _ => TimeSpan.MinValue,
            };
        }
        catch (OverflowException)
        {
            return false;
        }

        if (value == TimeSpan.MinValue)
        {
            return false;
        }

        result = new DurationString(value, text.Trim());
        return true;
    }

    /// <inheritdoc />
    public bool Equals(DurationString other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DurationString other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => Text;

    private const int SecondsPerMinute = 60;
    private const int MinutesPerHour = 60;

    private static string Format(TimeSpan value)
    {
        if (value.Milliseconds != 0 || (value.Ticks != 0 && value < TimeSpan.FromSeconds(1)))
        {
            return $"{(long)value.TotalMilliseconds}ms";
        }

        var totalSeconds = (long)value.TotalSeconds;
        if (totalSeconds % SecondsPerMinute != 0)
        {
            return $"{totalSeconds}s";
        }

        var totalMinutes = totalSeconds / SecondsPerMinute;
        if (totalMinutes % MinutesPerHour != 0)
        {
            return $"{totalMinutes}m";
        }

        return $"{totalMinutes / MinutesPerHour}h";
    }
}
