// -----------------------------------------------------------------------
// <copyright file="TemplateExpander.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace AuthCallout.Core.Templates;

/// <summary>
/// Expands <c>publishTemplates</c>/<c>subscribeTemplates</c> subject patterns by substituting
/// each <c>{name}</c> placeholder with its resolved <see cref="VariableResolver"/> output
/// (ADR 0009 §Templates and expansion).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>A template with no placeholders expands to itself, once.</description></item>
/// <item><description>A template referencing one multi-valued variable expands once per value.</description></item>
/// <item><description>A template referencing several multi-valued variables expands over their
/// <b>cross-product</b> (every combination of one value per referenced variable).</description></item>
/// <item><description>A template referencing a variable that resolved to <b>zero</b> values
/// emits <b>nothing</b> for that template (an empty cross-product), per ADR 0009 — this is
/// distinct from (and checked before) whether the variable was <c>required</c>, which
/// <see cref="VariableResolver"/> already turned into a denial upstream if applicable; by the
/// time expansion runs, a present-but-empty resolution can only be a non-required variable.</description></item>
/// <item><description>Output is sorted ordinally for deterministic, testable results — the
/// cross-product enumeration order itself is not a contract.</description></item>
/// <item><description>A placeholder naming an undeclared variable is a config-validation error
/// (P02's <c>ConfigValidator</c>, <c>unknown-template-variable</c>) that must never reach this
/// engine; <see cref="Expand"/> throws <see cref="KeyNotFoundException"/> defensively rather
/// than silently emitting a malformed subject.</description></item>
/// </list>
/// </remarks>
public static class TemplateExpander
{
    private static readonly Regex PlaceholderPattern = new(
        @"\{(?<name>[A-Za-z0-9_]+)\}",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>Expands every template in <paramref name="templates"/> and returns the flattened, sorted, de-duplicated result.</summary>
    /// <param name="templates">The subject-pattern templates to expand (e.g. a profile's <c>publishTemplates</c>).</param>
    /// <param name="resolvedVariables">Every declared variable's resolved values, keyed by name (<see cref="VariableResolver.TryResolveAll"/>).</param>
    /// <returns>Every expanded subject, sorted ordinally, with duplicates across templates removed.</returns>
#pragma warning disable S4017 // Dictionary<string, IReadOnlyList<string>> is VariableResolver's actual output shape (mirrors ClaimMatch.Predicates in Contracts); a wrapper type would just re-expose the same two generic parameters.
    public static IReadOnlyList<string> ExpandAll(
        IReadOnlyList<string> templates, IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedVariables)
#pragma warning restore S4017
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(resolvedVariables);

        var subjects = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var template in templates)
        {
            foreach (var subject in Expand(template, resolvedVariables))
            {
                subjects.Add(subject);
            }
        }

        return [.. subjects];
    }

    /// <summary>Expands one template against resolved variable values.</summary>
    /// <param name="template">The subject-pattern template, e.g. <c>mise.house.{tenant}.user.{user}.commands</c>.</param>
    /// <param name="resolvedVariables">Every declared variable's resolved values, keyed by name.</param>
    /// <returns>Every expanded subject for this template, sorted ordinally. Empty when a referenced variable resolved to zero values.</returns>
    /// <exception cref="KeyNotFoundException">The template references a variable name absent from <paramref name="resolvedVariables"/> (config-validation should have already rejected this).</exception>
#pragma warning disable S4017 // See ExpandAll: this is VariableResolver's actual output shape, not incidental nesting.
    public static IReadOnlyList<string> Expand(string template, IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedVariables)
#pragma warning restore S4017
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(resolvedVariables);

        var names = ExtractPlaceholderNames(template);
        if (names.Count == 0)
        {
            return [template];
        }

        var valueSets = new List<IReadOnlyList<string>>(names.Count);
        foreach (var name in names)
        {
            if (!resolvedVariables.TryGetValue(name, out var values))
            {
                throw new KeyNotFoundException(
                    $"Template '{template}' references undeclared variable '{{{name}}}'; this should have been rejected at config load.");
            }

            if (values.Count == 0)
            {
                // ADR 0009: a variable with no value means the template emits nothing.
                return [];
            }

            valueSets.Add(values);
        }

        var results = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var combination in CrossProduct(valueSets))
        {
            results.Add(Substitute(template, names, combination));
        }

        return [.. results];
    }

    private static IReadOnlyList<string> ExtractPlaceholderNames(string template)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in PlaceholderPattern.Matches(template))
        {
            var name = match.Groups["name"].Value;
            if (seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static string Substitute(string template, IReadOnlyList<string> names, IReadOnlyList<string> values)
    {
        var result = template;
        for (var i = 0; i < names.Count; i++)
        {
            result = result.Replace($"{{{names[i]}}}", values[i], StringComparison.Ordinal);
        }

        return result;
    }

#pragma warning disable S4017 // A list of value-sets is the natural shape for a cross-product; a named wrapper adds no clarity here.
    private static IEnumerable<IReadOnlyList<string>> CrossProduct(IReadOnlyList<IReadOnlyList<string>> valueSets)
#pragma warning restore S4017
    {
        var indices = new int[valueSets.Count];
        var hasMore = valueSets.Count == 0 || valueSets.All(static set => set.Count > 0);
        while (hasMore)
        {
            var combination = new string[valueSets.Count];
            for (var i = 0; i < valueSets.Count; i++)
            {
                combination[i] = valueSets[i][indices[i]];
            }

            yield return combination;

            hasMore = Advance(indices, valueSets);
        }
    }

#pragma warning disable S4017 // Same value-set shape as CrossProduct above.
    private static bool Advance(int[] indices, IReadOnlyList<IReadOnlyList<string>> valueSets)
#pragma warning restore S4017
    {
        for (var position = valueSets.Count - 1; position >= 0; position--)
        {
            indices[position]++;
            if (indices[position] < valueSets[position].Count)
            {
                return true;
            }

            indices[position] = 0;
        }

        return false;
    }
}
