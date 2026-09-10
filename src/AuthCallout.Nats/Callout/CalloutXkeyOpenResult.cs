// -----------------------------------------------------------------------
// <copyright file="CalloutXkeyOpenResult.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Nats.Callout;

/// <summary>The outcome of <see cref="CalloutXkeyEnvelope.Open"/>: either an accepted plaintext payload, or a rejection reason.</summary>
public readonly struct CalloutXkeyOpenResult : IEquatable<CalloutXkeyOpenResult>
{
    private CalloutXkeyOpenResult(bool accepted, byte[]? payload, string? serverXkeyPublic, string? rejectionReason)
    {
        Accepted = accepted;
        Payload = payload;
        ServerXkeyPublic = serverXkeyPublic;
        RejectionReason = rejectionReason;
    }

    /// <summary>Gets a value indicating whether the request was accepted (not an ADR 0018 mismatch).</summary>
    public bool Accepted { get; }

    /// <summary>Gets the plaintext payload, when <see cref="Accepted"/> is true.</summary>
#pragma warning disable CA1819 // Raw message payload, matching CalloutCryptoResult/NATS.Net's byte[]-based message API.
    public byte[]? Payload { get; }
#pragma warning restore CA1819

    /// <summary>Gets the server's ephemeral public xkey, when the request was encrypted; null for a plaintext request.</summary>
    public string? ServerXkeyPublic { get; }

    /// <summary>Gets the human-readable rejection reason, when <see cref="Accepted"/> is false.</summary>
    public string? RejectionReason { get; }

    public static bool operator ==(CalloutXkeyOpenResult left, CalloutXkeyOpenResult right) => left.Equals(right);

    public static bool operator !=(CalloutXkeyOpenResult left, CalloutXkeyOpenResult right) => !left.Equals(right);

    /// <summary>Creates an accepted result.</summary>
    /// <param name="payload">The plaintext payload.</param>
    /// <param name="serverXkeyPublic">The server's ephemeral public xkey, or null for a plaintext request.</param>
    /// <returns>The constructed accepted result.</returns>
    public static CalloutXkeyOpenResult Accept(byte[] payload, string? serverXkeyPublic) => new(true, payload, serverXkeyPublic, rejectionReason: null);

    /// <summary>Creates a rejected result.</summary>
    /// <param name="reason">The human-readable rejection reason.</param>
    /// <returns>The constructed rejected result.</returns>
    public static CalloutXkeyOpenResult Reject(string reason) => new(false, payload: null, serverXkeyPublic: null, reason);

    /// <inheritdoc />
    public bool Equals(CalloutXkeyOpenResult other) =>
        Accepted == other.Accepted
        && ReferenceEquals(Payload, other.Payload)
        && string.Equals(ServerXkeyPublic, other.ServerXkeyPublic, StringComparison.Ordinal)
        && string.Equals(RejectionReason, other.RejectionReason, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CalloutXkeyOpenResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Accepted, Payload, ServerXkeyPublic, RejectionReason);
}
