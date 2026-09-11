// -----------------------------------------------------------------------
// <copyright file="TrustedHttpClientFactory.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;

namespace AuthCallout.Integration.Support;

/// <summary>
/// Builds an <see cref="HttpClient"/> that trusts exactly one certificate (a fixture's
/// throwaway self-signed CA) via <see cref="X509ChainTrustMode.CustomRootTrust"/>, rather
/// than mutating the OS/user certificate store (which was observed to be racy/unreliable for
/// newly-added <c>CurrentUser</c> <see cref="StoreName.Root"/> entries on Linux — see
/// <see href="https://github.com/dotnet/runtime/issues/32608"/>) or the machine-wide
/// <c>SSL_CERT_FILE</c> environment variable (observed not to affect .NET's own managed
/// <see cref="X509Chain"/> validation here). Every real HTTPS client this suite builds
/// against a self-signed fixture (<c>KeycloakFixture</c>'s own token-fetch calls, and the
/// callout Host's JWKS fetch via <c>IssuerConfigurationManagerFactory</c>'s test-seam
/// constructor) shares this one trust mechanism.
/// </summary>
public static class TrustedHttpClientFactory
{
    /// <summary>Creates an <see cref="HttpClient"/> that validates server certificates against exactly <paramref name="trustedCertificatePem"/>, nothing else.</summary>
    /// <param name="trustedCertificatePem">The PEM-encoded self-signed CA certificate to trust.</param>
    /// <returns>The configured client. Caller owns disposal.</returns>
#pragma warning disable CA2000 // The handler is handed to the returned HttpClient with disposeHandler: true, which owns and disposes it - the same "hand off ownership" pattern CalloutCrypto/KvIdentityStoreWriter document at their own CA2000 suppressions.
    public static HttpClient Create(string trustedCertificatePem) =>
        new(CreateHandler(trustedCertificatePem), disposeHandler: true);
#pragma warning restore CA2000

    /// <summary>
    /// Creates the underlying <see cref="HttpMessageHandler"/> alone (rather than a full
    /// <see cref="HttpClient"/>), for callers that need to hand it to another API expecting
    /// a handler — e.g. Testcontainers' <c>HttpWaitStrategy.UsingHttpMessageHandler</c>.
    /// </summary>
    /// <param name="trustedCertificatePem">The PEM-encoded self-signed CA certificate to trust.</param>
    /// <returns>The configured handler. Caller owns disposal.</returns>
    public static HttpMessageHandler CreateHandler(string trustedCertificatePem)
    {
        var trustedCertificate = X509CertificateLoader.LoadCertificate(System.Text.Encoding.UTF8.GetBytes(trustedCertificatePem));
        return new SocketsHttpHandler
        {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) => ValidateAgainst(trustedCertificate, certificate),
            },
        };
    }

    private static bool ValidateAgainst(X509Certificate2 trustedCertificate, System.Security.Cryptography.X509Certificates.X509Certificate? presented)
    {
        if (presented is null)
        {
            return false;
        }

        using var presentedV2 = new X509Certificate2(presented);
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(trustedCertificate);
        return chain.Build(presentedV2);
    }
}
