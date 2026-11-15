// -----------------------------------------------------------------------
// <copyright file="TestJwt.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using System.Text.Json;

namespace AuthCallout.Client.Tests;

/// <summary>
/// Builds unsigned-but-well-formed compact JWTs for tests of claim-reading code
/// (<see cref="AuthCallout.Client.Tokens.OidcTokenClaims"/>) that never validates a signature.
/// </summary>
public static class TestJwt
{
    /// <summary>Builds a compact JWT (header.payload.signature) with the given header/payload claims and an arbitrary signature segment.</summary>
    /// <param name="payloadClaims">The payload claims.</param>
    /// <param name="kid">Optional <c>kid</c> header claim.</param>
    /// <returns>The compact JWT string.</returns>
    public static string Create(IDictionary<string, object?> payloadClaims, string? kid = null)
    {
        var header = new Dictionary<string, object?>(StringComparer.Ordinal) { ["alg"] = "none", ["typ"] = "JWT" };
        if (kid is not null)
        {
            header["kid"] = kid;
        }

        var headerSegment = Encode(header);
        var payloadSegment = Encode(payloadClaims);
        return $"{headerSegment}.{payloadSegment}.signature";
    }

    private static string Encode(IDictionary<string, object?> claims)
    {
        var json = JsonSerializer.Serialize(claims);
        return Base64UrlEncode(Encoding.UTF8.GetBytes(json));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
