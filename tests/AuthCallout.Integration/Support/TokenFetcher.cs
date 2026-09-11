// -----------------------------------------------------------------------
// <copyright file="TokenFetcher.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;

namespace AuthCallout.Integration.Support;

/// <summary>
/// Fetches a real access token from Keycloak's token endpoint over HTTPS. Relies on
/// <see cref="Fixtures.KeycloakFixture.TrustRootCertificate"/> already having been called
/// (the default <see cref="HttpClient"/> validates against the process's trusted roots,
/// which includes the fixture's self-signed CA once <c>SSL_CERT_FILE</c> is set).
/// </summary>
public static class TokenFetcher
{
    /// <summary>Posts a token request to <c>{baseAddress}/realms/{realm}/protocol/openid-connect/token</c> and returns the raw <c>access_token</c>.</summary>
    /// <param name="baseAddress">The Keycloak HTTPS base address.</param>
    /// <param name="realm">The realm name.</param>
    /// <param name="formFields">The token-request form fields (grant type, client id, credentials).</param>
    /// <returns>The raw access token string.</returns>
    public static async Task<string> FetchAsync(string baseAddress, string realm, IReadOnlyDictionary<string, string> formFields)
    {
        using var client = new HttpClient();
        var tokenUrl = $"{baseAddress}/realms/{realm}/protocol/openid-connect/token";
        using var content = new FormUrlEncodedContent(formFields);
        using var response = await client.PostAsync(new Uri(tokenUrl), content).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token request to '{tokenUrl}' failed ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException($"Token response from '{tokenUrl}' had no 'access_token'.");
    }
}
