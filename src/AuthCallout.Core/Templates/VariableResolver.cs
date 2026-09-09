// -----------------------------------------------------------------------
// <copyright file="VariableResolver.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Core.Templates;

/// <summary>
/// Resolves a profile's <see cref="TemplateVariable"/> declarations from validated claims,
/// applying each variable's transform pipeline to every claim value (ADR 0009 §Variable).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>A variable bound to a scalar claim resolves to at most one transformed value; one bound to a multi-valued (array) claim resolves to one transformed value per claim value, in the claim's own order (before any output-ordering pass in <see cref="TemplateExpander"/>).</description></item>
/// <item><description>A <c>required</c> variable that resolves to zero values (claim absent, or every value's <c>require-safe</c> step in fact never runs because there are no values to run it on) denies the connection with <see cref="DenyReason.RequiredVariableMissing"/>.</description></item>
/// <item><description>A value whose transform pipeline fails a <c>require-safe</c> step denies the connection with <see cref="DenyReason.UnsafeTemplateValue"/>, regardless of whether the variable is <c>required</c> — an unsafe value is never silently dropped (ADR 0009: "an unsafe result is a denial, never a silently malformed grant").</description></item>
/// <item><description>An optional variable that resolves to zero values is not an error; it simply contributes no values, and any template referencing it emits nothing for it (<see cref="TemplateExpander"/>).</description></item>
/// </list>
/// </remarks>
public static class VariableResolver
{
    /// <summary>
    /// Resolves every variable in <paramref name="variables"/> from <paramref name="claims"/>.
    /// </summary>
    /// <param name="variables">The profile's declared template variables.</param>
    /// <param name="claims">The validated claims to resolve variables from.</param>
    /// <param name="resolved">Every variable's transformed value set, keyed by <see cref="TemplateVariable.Name"/>, when this returns null.</param>
    /// <returns>Null on success (see <paramref name="resolved"/>); the specific <see cref="DenyReason"/> when a <c>required</c> variable is missing or a value is unsafe.</returns>
#pragma warning disable CA1021, S3874, S4017 // Out param + nested generic mirror this packet's TryXxx pattern (Transform.TryParse); avoids a bespoke result wrapper type for a two-field outcome.
    public static DenyReason? TryResolveAll(
        IReadOnlyList<TemplateVariable> variables,
        IClaimsAccessor claims,
        out IReadOnlyDictionary<string, IReadOnlyList<string>> resolved)
#pragma warning restore CA1021, S3874, S4017
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(claims);

        var result = new Dictionary<string, IReadOnlyList<string>>(variables.Count, StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            var denyReason = TryResolveOne(variable, claims, out var values);
            if (denyReason is not null)
            {
                resolved = result;
                return denyReason;
            }

            result[variable.Name] = values;
        }

        resolved = result;
        return null;
    }

    /// <summary>Resolves a single <see cref="TemplateVariable"/> from <paramref name="claims"/>.</summary>
    /// <param name="variable">The variable to resolve.</param>
    /// <param name="claims">The validated claims to read the variable's claim path from.</param>
    /// <param name="values">The variable's transformed values (possibly empty, for a non-required variable with no claim value).</param>
    /// <returns>Null on success; <see cref="DenyReason.RequiredVariableMissing"/> or <see cref="DenyReason.UnsafeTemplateValue"/> on failure.</returns>
#pragma warning disable CA1021, S3874 // Out param mirrors this packet's TryXxx pattern (Transform.TryParse).
    public static DenyReason? TryResolveOne(TemplateVariable variable, IClaimsAccessor claims, out IReadOnlyList<string> values)
#pragma warning restore CA1021, S3874
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(claims);

        var rawValues = claims.GetValues(variable.Claim);
        if (rawValues.Count == 0)
        {
            values = [];
            return variable.Required ? DenyReason.RequiredVariableMissing : null;
        }

        var transformed = new List<string>(rawValues.Count);
        foreach (var rawValue in rawValues)
        {
            if (!TransformPipeline.TryApply(variable.Transforms, rawValue, out var transformedValue))
            {
                values = [];
                return DenyReason.UnsafeTemplateValue;
            }

            transformed.Add(transformedValue);
        }

        values = transformed;
        return null;
    }
}
