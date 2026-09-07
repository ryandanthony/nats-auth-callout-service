// -----------------------------------------------------------------------
// <copyright file="TransformKind.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Config;

/// <summary>
/// The fixed set of named transform steps a <see cref="TemplateVariable"/> may apply, in
/// order, to a claim value (ADR 0009 §Transform pipeline). The set is fixed and defined by
/// the service, not arbitrary code; an unrecognized transform name is a config-load error
/// (caught by <see cref="Transform.TryParse"/> / the aggregate validator).
/// </summary>
public enum TransformKind
{
    /// <summary>Lowercases the value.</summary>
    Lowercase,

    /// <summary>Uppercases the value.</summary>
    Uppercase,

    /// <summary>Replaces every occurrence of <see cref="Transform.From"/> with <see cref="Transform.To"/>.</summary>
    Replace,

    /// <summary>Maps any character outside <c>[A-Za-z0-9_-]</c> (and NATS-special <c>.</c>/<c>*</c>/<c>&gt;</c>/whitespace) to <c>_</c>.</summary>
    Sanitize,

    /// <summary>Replaces the value with a stable SHA-256 hex digest (see the P04 <c>Templates</c> engine for the exact digest length).</summary>
    Hash,

    /// <summary>Prepends <see cref="Transform.Text"/> to the value.</summary>
    Prefix,

    /// <summary>Appends <see cref="Transform.Text"/> to the value.</summary>
    Suffix,

    /// <summary>Denies the connection (<see cref="Decisions.DenyReason.UnsafeTemplateValue"/>) if the value is not, at this point in the pipeline, a single safe subject token.</summary>
    RequireSafe,
}
