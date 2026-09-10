// -----------------------------------------------------------------------
// <copyright file="Doc03DecisionTreeCase.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Core.Tests.Pipeline;

/// <summary>
/// One row of <see cref="ResolutionPipelineTests"/>' doc 02 / ADR 0003 decision-tree deny
/// table. A top-level type (rather than nested in the test class) because the repo's
/// analyzers (CA1034) forbid externally-visible nested types, and xUnit's <c>[MemberData]</c>
/// theory rows must be a public type.
/// </summary>
/// <param name="ValidatorOutcome">The substitute <see cref="ITokenValidator"/>'s canned outcome; null leaves the substitute unconfigured (used only when the pipeline must deny before ever calling it, e.g. an unknown issuer).</param>
/// <param name="ExpectedDeny">The expected <see cref="DenyReason"/> on the resulting <see cref="DeniedDecision"/>.</param>
/// <param name="TokenIssuer">Overrides the token's <c>iss</c>; defaults to the fixture's configured issuer.</param>
public sealed record Doc03DecisionTreeCase(TokenValidationOutcome? ValidatorOutcome, DenyReason ExpectedDeny, string? TokenIssuer = null)
{
    /// <inheritdoc/>
    public override string ToString() => ExpectedDeny.ToString();
}
