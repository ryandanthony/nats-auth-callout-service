// -----------------------------------------------------------------------
// <copyright file="IConnectionIdGenerator.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Primitives;

/// <summary>
/// Generates the callout-controlled, high-entropy connection id used as the token-store
/// correlation key (ADR 0010: "provider-neutral... callout-controlled... per-connection").
/// Not a token claim. Implemented by P08 (<c>ConnectionIdGenerator</c>); consumed by the
/// resolution pipeline / identity store writer (P05, P07, P08).
/// </summary>
public interface IConnectionIdGenerator
{
    /// <summary>
    /// Generates a new, subject-safe (no <c>.</c>/<c>*</c>/<c>&gt;</c>), high-entropy
    /// (128-bit or greater) id, unique per invocation.
    /// </summary>
    /// <returns>A newly generated, subject-safe, high-entropy connection id.</returns>
    string Generate();
}
