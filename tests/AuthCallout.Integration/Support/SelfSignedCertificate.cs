// -----------------------------------------------------------------------
// <copyright file="SelfSignedCertificate.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AuthCallout.Integration.Support;

/// <summary>
/// Generates a throwaway, self-signed (self-CA) certificate for <c>KeycloakFixture</c>'s
/// HTTPS endpoint. The callout Host's production JWKS fetch path
/// (<see cref="AuthCallout.Identity.Jwks.IssuerConfigurationManagerFactory"/>'s parameterless
/// constructor) always requires HTTPS — matching real deployments and the dev AppHost's own
/// "Keycloak must be served over HTTPS" note — so this suite serves Keycloak over real TLS
/// rather than relaxing that requirement. The certificate is marked as its own CA
/// (<c>basicConstraints: CA:true</c>) purely so it can double as the trust anchor: the test
/// process trusts it via the <c>SSL_CERT_FILE</c> environment variable (OpenSSL's env-var
/// override, which .NET's Linux TLS stack honors), not via the OS certificate store, keeping
/// the whole thing self-contained and CI-safe (no <c>sudo</c>/system store mutation). Returns
/// only the PEM text (never the live <see cref="X509Certificate2"/>/<see cref="RSA"/>
/// handles) — callers only ever need the exported bytes to write into a resource mapping or
/// a trust-anchor file, so there is nothing left to dispose once generation returns.
/// </summary>
public static class SelfSignedCertificate
{
    /// <summary>Generates a fresh self-signed CA certificate and RSA key pair.</summary>
    /// <returns>The PEM-encoded certificate and private key.</returns>
    public static (string certificatePem, string privateKeyPem) Generate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, critical: true));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        return (certificate.ExportCertificatePem(), rsa.ExportRSAPrivateKeyPem());
    }
}
