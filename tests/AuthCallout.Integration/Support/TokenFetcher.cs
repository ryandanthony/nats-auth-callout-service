// -----------------------------------------------------------------------
// <copyright file="TokenFetcher.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;

namespace AuthCallout.Integration.Support;

/// <summary>
/// Fetches a real access token from Keycloak's token endpoint over HTTPS, trusting only the
/// fixture's own self-signed CA (<see cref="TrustedHttpClientFactory"/>) rather than the
/// process's ambient trust roots.
/// </summary>
public static class TokenFetcher
{
    /// <summary>Posts a token request to <c>{baseAddress}/realms/{realm}/protocol/openid-connect/token</c> and returns the raw <c>access_token</c>.</summary>
    /// <param name="baseAddress">The Keycloak HTTPS base address.</param>
    /// <param name="realm">The realm name.</param>
    /// <param name="trustedCertificatePem">The PEM-encoded self-signed CA certificate to trust for this call.</param>
    /// <param name="formFields">The token-request form fields (grant type, client id, credentials).</param>
    /// <returns>The raw access token string.</returns>
    public static async Task<string> FetchAsync(
        string baseAddress, string realm, string trustedCertificatePem, IReadOnlyDictionary<string, string> formFields)
    {
        using var client = TrustedHttpClientFactory.Create(trustedCertificatePem);
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
