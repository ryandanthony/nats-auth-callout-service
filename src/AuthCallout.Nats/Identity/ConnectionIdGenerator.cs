// -----------------------------------------------------------------------
// <copyright file="ConnectionIdGenerator.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers.Text;
using System.Security.Cryptography;

namespace AuthCallout.Nats.Identity;

/// <summary>
/// Generates the high-entropy, callout-controlled connection id (ADR 0010 §Why a generated id
/// "not any provider claim") used as the key into the OIDC token store. Not a token claim, not
/// derived from anything in the request; purely a correlation key the callout owns the
/// format, uniqueness, and lifetime of.
/// </summary>
public static class ConnectionIdGenerator
{
    /// <summary>The number of random bytes generated per id: 128 bits (ADR 0010's "high-entropy GUID" requirement, at least 128-bit per the P08 packet).</summary>
    private const int RandomByteCount = 16;

    /// <summary>
    /// Generates a new high-entropy connection id: 128 bits from
    /// <see cref="RandomNumberGenerator"/>, Base64Url-encoded (URL/subject-safe: no
    /// <c>+</c>/<c>/</c>/<c>=</c> padding, and critically no NATS subject-token characters
    /// <c>.</c>/<c>*</c>/<c>&gt;</c>, so the id is always safe to use unescaped as a KV key
    /// segment, doc 07 <c>{id}</c>).
    /// </summary>
    /// <returns>A newly generated, URL/subject-safe connection id.</returns>
    public static string NewId()
    {
        Span<byte> bytes = stackalloc byte[RandomByteCount];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url.EncodeToString(bytes);
    }
}
