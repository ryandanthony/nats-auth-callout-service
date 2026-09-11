// -----------------------------------------------------------------------
// <copyright file="JwtInspector.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers.Text;
using System.Text.Json;

namespace AuthCallout.Integration.Support;

/// <summary>
/// Reads a single claim from an unverified compact JWT (header.payload.signature), for
/// scenario assertions that need a claim value already known to be correct (the token came
/// from a real, already-trusted Keycloak issuer) without re-implementing signature
/// verification in test code.
/// </summary>
public static class JwtInspector
{
    /// <summary>Reads a string claim from a compact JWT's payload segment, without verifying its signature.</summary>
    /// <param name="jwt">The compact JWT.</param>
    /// <param name="claimName">The claim name to read.</param>
    /// <returns>The claim's string value.</returns>
    /// <exception cref="FormatException">The token is not a well-formed compact JWT or lacks the requested claim.</exception>
    public static string ReadClaim(string jwt, string claimName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jwt);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimName);

        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            throw new FormatException($"'{jwt[..Math.Min(20, jwt.Length)]}...' is not a well-formed compact JWT.");
        }

        var payloadBytes = Base64Url.DecodeFromChars(parts[1]);
        using var doc = JsonDocument.Parse(payloadBytes);
        return doc.RootElement.TryGetProperty(claimName, out var value)
            ? value.GetString() ?? throw new FormatException($"Claim '{claimName}' is not a string.")
            : throw new FormatException($"Token has no '{claimName}' claim.");
    }
}
