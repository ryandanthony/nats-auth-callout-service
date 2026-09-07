// -----------------------------------------------------------------------
// <copyright file="CalloutCryptoResult.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// The result of an xkey seal/open operation (ADR 0018). Both directions are represented
/// by the same shape — the bytes to send/use next.
/// </summary>
/// <param name="Payload">The sealed (encrypted) or opened (decrypted) bytes.</param>
#pragma warning disable CA1819 // Raw message payload, matching NATS.Net's byte[]-based message API (NatsMsg<byte[]>); a wrapper collection buys nothing on this hot path.
public sealed record CalloutCryptoResult(byte[] Payload);
#pragma warning restore CA1819
