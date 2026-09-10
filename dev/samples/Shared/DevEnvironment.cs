// -----------------------------------------------------------------------
// <copyright file="DevEnvironment.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Dev.Samples.Common;

/// <summary>Small environment-variable readers shared by both P15 sample apps.</summary>
public static class DevEnvironment
{
    /// <summary>Gets the default NATS URL used when neither Aspire nor an explicit override is present.</summary>
    /// <value>The default NATS URL, <c>nats://localhost:4222</c> (AppHost.Local's external port, doc 08).</value>
    public static string DefaultNatsUrl => "nats://localhost:4222";

    /// <summary>Gets the default Keycloak base URL used when Aspire's injected value is absent.</summary>
    /// <value>The default Keycloak base URL, <c>http://localhost:8080</c> (Aspire's <c>AddKeycloak("idp", 8080)</c>, doc 08).</value>
    public static string DefaultKeycloakBaseUrl => "http://localhost:8080";

    /// <summary>
    /// Reads the Keycloak base URL: Aspire's injected service-discovery variable for the
    /// resource named <c>idp</c> if present, else <see cref="DefaultKeycloakBaseUrl"/>.
    /// </summary>
    /// <returns>The effective Keycloak base URL.</returns>
    public static string ReadKeycloakBaseUrl() =>
        Environment.GetEnvironmentVariable("services__idp__http__0") ?? DefaultKeycloakBaseUrl;

    /// <summary>
    /// Reads the NATS connection URL: Aspire's injected <c>NATS_URI</c> (doc 08 §AppHost.Local
    /// `WithReference(nats)`) if present, else <see cref="DefaultNatsUrl"/> for running the
    /// sample directly against a manually-started <c>nats-server</c>.
    /// </summary>
    /// <returns>The effective NATS connection URL.</returns>
    public static string ReadNatsUrl() =>
        Environment.GetEnvironmentVariable("NATS_URI") ?? DefaultNatsUrl;
}
