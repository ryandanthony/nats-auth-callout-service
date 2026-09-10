// -----------------------------------------------------------------------
// <copyright file="SyntheticRequest.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Nats.Tests.Callout;

/// <summary>One synthetic request for <see cref="ResponderHarness.RunManyAsync(IReadOnlyList{SyntheticRequest})"/>.</summary>
/// <param name="Payload">The raw (possibly xkey-sealed) request payload bytes.</param>
/// <param name="XkeyHeaderValue">The <c>Nats-Server-Xkey</c> header value to attach, or null for a plaintext request.</param>
internal sealed record SyntheticRequest(byte[] Payload, string? XkeyHeaderValue);
