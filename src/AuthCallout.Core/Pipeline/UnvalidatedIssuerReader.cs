// -----------------------------------------------------------------------
// <copyright file="UnvalidatedIssuerReader.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers.Text;
using System.Text.Json;

namespace AuthCallout.Core.Pipeline;

/// <summary>
/// Reads the <c>iss</c> claim from a raw bearer token's payload segment **without**
/// verifying its signature (doc 03 Stage 1: "Read <c>iss</c> from the token without
/// trusting it"). This is the one place the pipeline looks inside an unvalidated token, and
/// it reads exactly one field for routing only (ADR 0003 §Trust boundary); every other
/// claim is read only from the <see cref="AuthCallout.Contracts.Seams.IClaimsAccessor"/>
/// <c>ITokenValidator</c> returns after signature verification succeeds. Uses only the BCL
/// (<see cref="Base64Url"/>, <c>System.Text.Json</c>) so <c>AuthCallout.Core</c> takes on no
/// JWT-library dependency for this pre-validation peek (doc 08 §reference-isolation).
/// </summary>
internal static class UnvalidatedIssuerReader
{
    /// <summary>
    /// Attempts to read the unvalidated <c>iss</c> claim from a raw JWT's payload segment.
    /// Never throws: a malformed token (wrong segment count, non-Base64Url payload, invalid
    /// JSON, a non-object payload, or a missing/non-string <c>iss</c>) simply yields null,
    /// which the pipeline treats identically to "no configured application matches this
    /// issuer" (doc 03 Stage 1, <see cref="AuthCallout.Contracts.Decisions.DenyReason.UnknownIssuer"/>)
    /// since there is no vocabulary for "malformed" separate from "unknown" issuer.
    /// </summary>
    /// <param name="rawToken">The raw, undecoded bearer token.</param>
    /// <returns>The unvalidated <c>iss</c> value, or null when it could not be read.</returns>
    internal static string? TryReadIssuer(string rawToken)
    {
        ArgumentNullException.ThrowIfNull(rawToken);

        var segments = rawToken.Split('.');
        if (segments.Length < 2)
        {
            return null;
        }

        try
        {
            var payloadBytes = Base64Url.DecodeFromChars(segments[1]);
            using var document = JsonDocument.Parse(payloadBytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return document.RootElement.TryGetProperty("iss", out var issElement) && issElement.ValueKind == JsonValueKind.String
                ? issElement.GetString()
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
