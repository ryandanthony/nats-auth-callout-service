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

    /// <summary>
    /// Reads the NATS connection URL: Aspire's injected <c>NATS_URI</c> (doc 08 §AppHost.Local
    /// `WithReference(nats)`) if present, else <see cref="DefaultNatsUrl"/> for running the
    /// sample directly against a manually-started <c>nats-server</c>.
    /// </summary>
    /// <returns>The effective NATS connection URL.</returns>
    public static string ReadNatsUrl() =>
        Environment.GetEnvironmentVariable("NATS_URI") ?? DefaultNatsUrl;

    /// <summary>Reads a required OIDC bearer token from the named environment variable, failing fast with a clear message if unset.</summary>
    /// <param name="variableName">The environment variable carrying the token.</param>
    /// <param name="expectedShapeDescription">A short description of the expected token (for the failure message).</param>
    /// <returns>The token value.</returns>
    /// <exception cref="InvalidOperationException">The variable is unset or empty.</exception>
    public static string RequireOidcToken(string variableName, string expectedShapeDescription)
    {
        var token = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException($"{variableName} must be set ({expectedShapeDescription}).");
        }

        return token;
    }
}
