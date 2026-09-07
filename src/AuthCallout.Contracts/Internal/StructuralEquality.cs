// -----------------------------------------------------------------------
// <copyright file="StructuralEquality.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using ClaimPredicateMap = System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<string>>;

namespace AuthCallout.Contracts.Internal;

/// <summary>
/// Sequence- and dictionary-aware equality helpers used by the canonical config
/// records to provide genuine value-equality (ADR 0007) for collection-valued
/// properties. The C# compiler's synthesized record equality compares
/// <see cref="System.Collections.Generic.IReadOnlyList{T}"/> and
/// <see cref="System.Collections.Generic.IReadOnlyDictionary{TKey,TValue}"/>
/// members by reference, not by content, so any record with a collection
/// property overrides <c>Equals</c>/<c>GetHashCode</c> using these helpers.
/// </summary>
internal static class StructuralEquality
{
    /// <summary>Order-sensitive sequence equality, null-safe (two nulls are equal).</summary>
    internal static bool SequenceEqual<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.SequenceEqual(right);
    }

    /// <summary>Order-sensitive hash code over a sequence, null-safe.</summary>
    internal static int SequenceHashCode<T>(IReadOnlyList<T>? sequence)
    {
        if (sequence is null)
        {
            return 0;
        }

        var hash = default(HashCode);
        foreach (var item in sequence)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Set-of-keys, order-sensitive-per-key-value equality for a dictionary whose
    /// values are themselves ordered lists (used by <see cref="Config.ClaimMatch"/>).
    /// </summary>
    internal static bool DictionaryOfListsEqual(
        ClaimPredicateMap? left,
        ClaimPredicateMap? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, values) in left)
        {
            if (!right.TryGetValue(key, out var otherValues) || !SequenceEqual(values, otherValues))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Order-insensitive-by-key hash code for a dictionary of ordered lists.</summary>
    internal static int DictionaryOfListsHashCode(ClaimPredicateMap? dictionary)
    {
        if (dictionary is null)
        {
            return 0;
        }

        // XOR combine per-entry hashes so the result is independent of enumeration order,
        // matching the order-insensitive-by-key equality above.
        var combined = 0;
        foreach (var (key, values) in dictionary)
        {
            var entryHash = HashCode.Combine(key, SequenceHashCode(values));
            combined ^= entryHash;
        }

        return combined;
    }
}
