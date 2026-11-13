// -----------------------------------------------------------------------
// <copyright file="AssertionSigner.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AuthCallout.Contracts.Seams;
using NATS.NKeys;

namespace AuthCallout.Identity.Assertions;

/// <summary>
/// <see cref="IAssertionSigner"/>: pure, no-I/O compact-JWT signer/verifier for identity
/// assertions (ADR 0011 §The assertion, ADR 0014 keyset rotation). Consumes an already-resolved
/// keyset (<see cref="ResolvedAssertionKey"/>) — exactly one <see cref="ResolvedAssertionKey.Active"/>
/// entry is required at construction (config-load validation already enforces this on the
/// bootstrap <c>AssertionKey</c> list; re-checked here defensively since this type can be
/// constructed directly in tests).
/// </summary>
public sealed partial class AssertionSigner : IAssertionSigner, IDisposable
{
    /// <summary>The fixed header <c>typ</c> (RFC 7519 §5.1), matching the repo's other compact-JWT producer (<c>NATS.Jwt</c>).</summary>
    private const string HeaderType = "JWT";

    /// <summary>The fixed header <c>alg</c>: Ed25519, distinct from NATS.Jwt's own <c>ed25519-nkey</c> string since this is a different, application-level token (ADR 0011: "not the NATS issuer signing seed").</summary>
    private const string HeaderAlgorithm = "EdDSA";

    /// <summary>The assertion's <c>schemaVersion</c> (ADR 0011: "reuse the ADR 0008 schemaVersion discipline"), doc 09-style additive versioning. D20: documented here, not a separate schema artifact.</summary>
    private const int SchemaVersion = 1;

    /// <summary>A compact JWT is header.payload.signature: exactly three dot-separated segments.</summary>
    private const int CompactJwtSegmentCount = 3;

    private readonly Dictionary<string, KeyPair> _verifyKeysByKid = new(StringComparer.Ordinal);
    private readonly string _activeKid;
    private readonly KeyPair _activeKeyPair;

    /// <summary>Initializes a new instance of the <see cref="AssertionSigner"/> class from a resolved keyset.</summary>
    /// <param name="keyset">The resolved assertion signing keyset; exactly one entry must be <see cref="ResolvedAssertionKey.Active"/> (ADR 0014).</param>
    /// <exception cref="ArgumentException">The keyset is empty, has zero or more than one active entry, or has duplicate <c>kid</c>s.</exception>
    public AssertionSigner(IReadOnlyList<ResolvedAssertionKey> keyset)
    {
        ArgumentNullException.ThrowIfNull(keyset);
        if (keyset.Count == 0)
        {
            throw new ArgumentException("Assertion keyset must not be empty.", nameof(keyset));
        }

        string? activeKid = null;
        foreach (var entry in keyset)
        {
            if (!_verifyKeysByKid.TryAdd(entry.Kid, KeyPair.FromSeed(entry.Seed)))
            {
                throw new ArgumentException($"Assertion keyset contains duplicate kid '{entry.Kid}'.", nameof(keyset));
            }

            if (entry.Active)
            {
                if (activeKid is not null)
                {
                    throw new ArgumentException("Assertion keyset must have exactly one active key (found more than one).", nameof(keyset));
                }

                activeKid = entry.Kid;
            }
        }

        if (activeKid is null)
        {
            throw new ArgumentException("Assertion keyset must have exactly one active key (found none).", nameof(keyset));
        }

        _activeKid = activeKid;
        _activeKeyPair = _verifyKeysByKid[activeKid];
    }

    /// <inheritdoc />
    public SignedAssertion Sign(AssertionClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var header = new AssertionHeader(HeaderType, HeaderAlgorithm, _activeKid);
        var payload = new AssertionPayload(SchemaVersion, claims.Id, claims.Subject, claims.Application, claims.Issuer, claims.Expiry.ToUnixTimeSeconds());

        var headerSegment = EncodeSegment(header, AssertionJsonContext.Default.AssertionHeader);
        var payloadSegment = EncodeSegment(payload, AssertionJsonContext.Default.AssertionPayload);
        var signingInput = $"{headerSegment}.{payloadSegment}";
        var signatureBytes = SignBytes(_activeKeyPair, signingInput);
        var signatureSegment = Base64Url.EncodeToString(signatureBytes);

        return new SignedAssertion($"{signingInput}.{signatureSegment}", _activeKid);
    }

    /// <inheritdoc />
    public AssertionClaims? Verify(string compactJwt, string publicKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compactJwt);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);

        var parts = compactJwt.Split('.');
        if (parts.Length != CompactJwtSegmentCount)
        {
            return null;
        }

        var (headerSegment, payloadSegment, signatureSegment) = (parts[0], parts[1], parts[^1]);

        AssertionHeader? header;
        AssertionPayload? payload;
        byte[] signatureBytes;
        try
        {
            header = JsonSerializer.Deserialize(Base64Url.DecodeFromChars(headerSegment), AssertionJsonContext.Default.AssertionHeader);
            payload = JsonSerializer.Deserialize(Base64Url.DecodeFromChars(payloadSegment), AssertionJsonContext.Default.AssertionPayload);
            signatureBytes = Base64Url.DecodeFromChars(signatureSegment);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }

        if (header is null || payload is null
            || !string.Equals(header.Alg, HeaderAlgorithm, StringComparison.Ordinal)
            || string.IsNullOrEmpty(header.Kid))
        {
            return null;
        }

        KeyPair verifierKeyPair;
        try
        {
            verifierKeyPair = KeyPair.FromPublicKey(publicKey);
        }
        catch (NKeysException)
        {
            return null;
        }

        var signingInput = $"{headerSegment}.{payloadSegment}";
        var verified = verifierKeyPair.Verify(Encoding.ASCII.GetBytes(signingInput), signatureBytes);
        if (!verified)
        {
            return null;
        }

        return new AssertionClaims(payload.Id, payload.Sub, payload.App, payload.Iss, DateTimeOffset.FromUnixTimeSeconds(payload.Exp));
    }

    /// <summary>Disposes every key pair in the keyset (both active and verify-only).</summary>
    public void Dispose()
    {
        foreach (var keyPair in _verifyKeysByKid.Values)
        {
            keyPair.Dispose();
        }
    }

    private static string EncodeSegment<T>(T value, JsonTypeInfo<T> typeInfo) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo));

    private static byte[] SignBytes(KeyPair keyPair, string signingInput)
    {
        var message = Encoding.ASCII.GetBytes(signingInput);
        var signature = new byte[64];
        keyPair.Sign(message, signature);
        return signature;
    }

    /// <summary>The assertion's JWT header: <c>typ</c>, <c>alg</c>, and the signing key's <c>kid</c> (ADR 0011).</summary>
    private sealed record AssertionHeader(string Typ, string Alg, string Kid);

    /// <summary>
    /// The assertion's JWT payload: exactly the ADR 0011 claim set plus <c>schemaVersion</c>
    /// (D20). The version property carries an explicit <see cref="JsonPropertyName"/> because
    /// the wire field is the documented interop contract, <c>schemaVersion</c> — the C#
    /// property name is longer only to avoid colliding with the type-level
    /// <see cref="SchemaVersion"/> constant, and that naming choice must never leak into the
    /// serialized payload (S4).
    /// </summary>
    private sealed record AssertionPayload(
        [property: JsonPropertyName("schemaVersion")] int PayloadSchemaVersion,
        string Id,
        string Sub,
        string App,
        string Iss,
        long Exp);

    /// <summary>
    /// Source-generated <see cref="JsonSerializerContext"/> for <see cref="AssertionHeader"/> and
    /// <see cref="AssertionPayload"/> (trim/R2R-safe: no reflection at serialize/deserialize time,
    /// doc 08 §Runtime &amp; trimming). Nested inside <see cref="AssertionSigner"/> because both
    /// target types are private to it. <see cref="JsonSourceGenerationOptionsAttribute.PropertyNamingPolicy"/>
    /// must be set here, on the generator attribute itself — setting
    /// <see cref="JsonSerializerOptions.PropertyNamingPolicy"/> on the runtime options passed to
    /// <see cref="JsonSerializer"/> has no effect on a source-generated contract, since the
    /// camelCase property names are baked in at compile time instead.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(AssertionHeader))]
    [JsonSerializable(typeof(AssertionPayload))]
    private sealed partial class AssertionJsonContext : JsonSerializerContext;
}
