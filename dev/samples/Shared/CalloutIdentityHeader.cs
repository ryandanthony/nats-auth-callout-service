// -----------------------------------------------------------------------
// <copyright file="CalloutIdentityHeader.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// The single constant both sample apps (and any future one) must agree on for the
/// identity-propagation message header (ADR 0010 §Message header naming convention:
/// <c>Callout-Identity</c>). In production a subscriber discovers this name from the
/// per-application discovery descriptor (ADR 0012) rather than hard-coding it; these
/// samples do both — they read the descriptor (doc 06 step "fetch discovery") for the
/// authoritative header name/bucket names and fall back to this constant only if the
/// descriptor is somehow unavailable, so the samples exercise the real discovery path
/// while staying simple to read.
/// </summary>
public static class CalloutIdentityHeader
{
    /// <summary>Gets the well-known fallback header name (ADR 0010), used only if discovery is unreachable.</summary>
    /// <value>The fallback header name, <c>Callout-Identity</c>.</value>
    public static string FallbackName => "Callout-Identity";
}
