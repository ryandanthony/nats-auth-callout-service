// -----------------------------------------------------------------------
// <copyright file="DiscoveryDescriptor.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// The client/subscriber-side shape of the per-application discovery descriptor
/// (doc 09 §2, ADR 0012, ADR 0013), read from <c>callout_apps_{slug}_discovery</c> /
/// <c>descriptor</c>. Both sample apps fetch this once per connection (doc 06: "Both
/// client and subscriber first <c>GET callout.apps.{app-slug}.discovery</c> (cached)")
/// rather than hard-coding bucket/header names, so the samples exercise the same
/// discovery contract a real integrating service would.
/// </summary>
/// <param name="SchemaVersion">The descriptor's <c>schemaVersion</c> (doc 09: currently <c>1</c>).</param>
/// <param name="App">The application slug.</param>
/// <param name="IdentityPropagation">Identity-propagation state and, when enabled, the discoverable facts.</param>
public sealed record DiscoveryDescriptor(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("app")] string App,
    [property: JsonPropertyName("identityPropagation")] DiscoveryIdentityPropagation IdentityPropagation)
{
    /// <summary>Deserializes a discovery descriptor from its published JSON bytes.</summary>
    /// <param name="json">The raw descriptor JSON, as published to the discovery bucket.</param>
    /// <returns>The parsed descriptor.</returns>
    /// <exception cref="JsonException">The JSON is malformed or does not match the expected shape.</exception>
    public static DiscoveryDescriptor Parse(byte[] json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<DiscoveryDescriptor>(json)
            ?? throw new JsonException("Discovery descriptor deserialized to null.");
    }
}
