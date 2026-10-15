namespace Test.Shared.Connectors.Http
{
    using System;
    using System.Net;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// Builds self-signed server certificates for 127.0.0.1 at run time.
    /// </summary>
    public static class HttpStubCertificate
    {
        /// <summary>
        /// Create a self-signed certificate with a usable private key.
        /// </summary>
        /// <param name="validDays">Days until expiry from now.</param>
        /// <returns>Certificate.</returns>
        public static X509Certificate2 Create(int validDays)
        {
            using (RSA rsa = RSA.Create(2048))
            {
                CertificateRequest request = new CertificateRequest("CN=hugo-http-stub", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
                san.AddIpAddress(IPAddress.Loopback);
                request.CertificateExtensions.Add(san.Build());
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
                DateTimeOffset now = DateTimeOffset.UtcNow;
                using (X509Certificate2 ephemeral = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(validDays)))
                {
                    byte[] pfx = ephemeral.Export(X509ContentType.Pkcs12, "hugo-stub");
#if NET9_0_OR_GREATER
                    return X509CertificateLoader.LoadPkcs12(pfx, "hugo-stub", X509KeyStorageFlags.Exportable);
#else
                    return new X509Certificate2(pfx, "hugo-stub", X509KeyStorageFlags.Exportable);
#endif
                }
            }
        }
    }
}
