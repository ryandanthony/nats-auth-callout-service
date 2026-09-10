// -----------------------------------------------------------------------
// <copyright file="RequestPayloadOptions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Nats.Tests.Callout;

/// <summary>Options for <see cref="CalloutTestBuilders.BuildRequestPayload"/>, grouped to stay within the parameter-count analyzer limit.</summary>
internal sealed record RequestPayloadOptions
{
    public string? UserNKey { get; init; }

    public string? RawToken { get; init; } = "raw-oidc-token";

    public string ServerId { get; init; } = "server-1";

    public string? Host { get; init; } = "1.2.3.4";
}
