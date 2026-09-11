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
    /// <value>The default Keycloak base URL, <c>https://localhost:8081</c> (AppHost.Local's <c>AddKeycloak("idp", 8081)</c>, doc 08).</value>
    public static string DefaultKeycloakBaseUrl => "https://localhost:8081";

    /// <summary>
    /// Reads the Keycloak base URL: Aspire's injected service-discovery variable for the
    /// resource named <c>idp</c> if present, else <see cref="DefaultKeycloakBaseUrl"/>. Aspire's
    /// <c>AddKeycloak</c> switches its endpoint to https in run mode whenever a dev HTTPS cert
    /// is available (see <c>AppHost.cs</c>'s own doc comment on why - JWKS retrieval requires
    /// it), which changes the injected variable's name from <c>services__idp__http__0</c> to
    /// <c>services__idp__https__0</c>; both are checked here so this sample works whichever
    /// scheme Aspire picked.
    /// </summary>
    /// <returns>The effective Keycloak base URL.</returns>
    public static string ReadKeycloakBaseUrl() =>
        Environment.GetEnvironmentVariable("services__idp__https__0")
        ?? Environment.GetEnvironmentVariable("services__idp__http__0")
        ?? DefaultKeycloakBaseUrl;

    /// <summary>
    /// Reads the NATS connection URL: Aspire's injected <c>NATS_URI</c> (doc 08 §AppHost.Local
    /// `WithReference(nats)`) if present, else <see cref="DefaultNatsUrl"/> for running the
    /// sample directly against a manually-started <c>nats-server</c>.
    /// </summary>
    /// <returns>The effective NATS connection URL.</returns>
    public static string ReadNatsUrl() =>
        Environment.GetEnvironmentVariable("NATS_URI") ?? DefaultNatsUrl;
}
