// -----------------------------------------------------------------------
// <copyright file="SignedAssertion.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// A signed identity assertion (ADR 0011): a compact JWT plus the <c>kid</c> of the key
/// that signed it.
/// </summary>
/// <param name="CompactJwt">The signed, compact JWT string.</param>
/// <param name="Kid">The signing key's id, also stamped in the JWT's header.</param>
public sealed record SignedAssertion(string CompactJwt, string Kid);
