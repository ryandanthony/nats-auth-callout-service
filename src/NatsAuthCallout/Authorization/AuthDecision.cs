// -----------------------------------------------------------------------
// <copyright file="AuthDecision.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace NatsAuthCallout.Authorization;

/// <summary>
/// The kind of principal an authorization decision was made for.
/// </summary>
public enum PrincipalKind
{
    /// <summary>A trusted service account (NKey allow-list match).</summary>
    ServiceAccount,

    /// <summary>An anonymous or unverifiable client.</summary>
    Anonymous,

    /// <summary>A client with a valid, in-scope bearer token.</summary>
    Authenticated,
}

/// <summary>
/// The pub/sub permissions and metadata to embed in a minted NATS user JWT.
/// </summary>
/// <param name="Kind">The principal classification.</param>
/// <param name="Name">The user JWT name (for observability).</param>
/// <param name="PublishAllow">Publish-allow subject patterns.</param>
/// <param name="SubscribeAllow">Subscribe-allow subject patterns.</param>
/// <param name="ExpiresAt">Absolute expiry for the minted JWT, if any.</param>
public sealed record AuthDecision(
    PrincipalKind Kind,
    string Name,
    IReadOnlyList<string> PublishAllow,
    IReadOnlyList<string> SubscribeAllow,
    DateTimeOffset? ExpiresAt);
