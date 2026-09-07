// -----------------------------------------------------------------------
// <copyright file="IdentityStoreWriteResult.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// The outcome of an identity-store write attempt (doc 03 §Store identity). P07's responder
/// treats a failed write as fail-closed (store-before-reply, doc 03): a grant whose identity
/// cannot be stored is denied rather than replied to without propagation, per the P07
/// packet's escalation note.
/// </summary>
/// <param name="Succeeded">True when the token/assertion writes (or the no-op skip, when propagation is disabled/anonymous) completed.</param>
/// <param name="ConnectionId">The generated connection id used as the token-store key, when a write occurred; null when skipped.</param>
public sealed record IdentityStoreWriteResult(bool Succeeded, string? ConnectionId);
