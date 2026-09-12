// -----------------------------------------------------------------------
// <copyright file="DevOidcTokenFetcher.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// Fetches an OIDC access token from the dev Keycloak realm via the Resource Owner Password
/// Credentials grant (dev-only convenience: a real client would run a browser redirect flow,
/// but a headless sample needs a token with no interactive step, and this realm's clients
/// are public with <c>directAccessGrantsEnabled</c> exactly to support it — see
/// <c>dev/AppHost.Local/realm/dev-realm.json</c>). Both sample apps use this instead of
/// requiring a pre-minted token via environment variable, so <c>dotnet run --project
/// dev/AppHost.Local</c> authorizes both samples with no manual step (P15 DoD).
/// </summary>
public static class DevOidcTokenFetcher
{
    /// <summary>Fetches an access token for the given public client + user credentials.</summary>
    /// <param name="keycloakBaseUrl">The Keycloak base URL (e.g. <c>http://localhost:8080</c>).</param>
    /// <param name="realm">The realm name (<c>dev</c>).</param>
    /// <param name="clientId">The public client id (<c>notes-web</c> or <c>tasks-web</c>).</param>
    /// <param name="username">The dev realm username.</param>
    /// <param name="password">The dev realm password (dev-only credential, committed to the realm import).</param>
    /// <param name="cancellationToken">Cancellation for the token request.</param>
    /// <returns>The raw OIDC access token.</returns>
    public static async Task<string> FetchAccessTokenAsync(
        string keycloakBaseUrl, string realm, string clientId, string username, string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keycloakBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(realm);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        using var http = new HttpClient();
        var tokenEndpoint = $"{keycloakBaseUrl.TrimEnd('/')}/realms/{realm}/protocol/openid-connect/token";
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = clientId,
            ["username"] = username,
            ["password"] = password,
            ["grant_type"] = "password",
        });

        using var response = await http.PostAsync(new Uri(tokenEndpoint), body, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        return payload?.AccessToken ?? throw new InvalidOperationException($"Token response from '{tokenEndpoint}' carried no access_token.");
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
