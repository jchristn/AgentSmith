namespace Hugo.Connectors.Docker
{
    using System;
    using System.Net.Security;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Docker.DotNet;
    using global::Docker.DotNet.X509;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Builds Docker clients from a resource's settings and bound secrets: a Unix socket, a named pipe, plain TCP, or
    /// TCP with TLS client authentication (the <c>tlsClientCertificate</c> slot). Reaching a daemon through an SSH
    /// resource tunnel is not supported yet. Thread-safe.
    /// </summary>
    public static class DockerSessionFactory
    {
        #region Public-Methods

        /// <summary>
        /// Parse and check an endpoint.
        /// </summary>
        /// <param name="endpoint">Endpoint text.</param>
        /// <returns>URI.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_setting</c> when the endpoint is not usable.</exception>
        public static Uri ParseEndpoint(string? endpoint)
        {
            if (String.IsNullOrWhiteSpace(endpoint)) throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The endpoint is empty. Use unix:///var/run/docker.sock or tcp://host:2376.");
            if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out Uri? uri))
                throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The endpoint '" + endpoint + "' is not a URI. Use unix:///var/run/docker.sock or tcp://host:2376.");
            switch (uri.Scheme)
            {
                case "unix":
                    if (String.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/") throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "A unix endpoint needs a socket path, for example unix:///var/run/docker.sock.");
                    return uri;
                case "npipe":
                    return uri;
                case "tcp":
                case "http":
                case "https":
                    if (String.IsNullOrEmpty(uri.Host) || uri.Port <= 0) throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "A tcp endpoint needs a host and port, for example tcp://10.0.0.5:2376.");
                    return uri;
                case "ssh":
                    throw new ConnectorException(ConnectorErrorCodes.NotSupported, "ssh:// endpoints are not supported yet. Expose the daemon on TCP with TLS, or run Hugo where it can reach the socket.");
                default:
                    throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "Unsupported endpoint scheme '" + uri.Scheme + "'. Use unix://, tcp://, or npipe://.");
            }
        }

        /// <summary>
        /// Load the TLS client credentials bound to <c>tlsClientCertificate</c>, or null when none are bound.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="tlsVerify">Whether to verify the daemon certificate (against <c>caCertificate</c> when present,
        /// otherwise against the system trust store).</param>
        /// <returns>Credentials, or null.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_setting</c> when the PEM material cannot be read.</exception>
        public static CertificateCredentials? LoadCredentials(ConnectorResourceContext resource, bool tlsVerify)
        {
            ArgumentNullException.ThrowIfNull(resource);
            string? certificatePem = resource.Secrets.Get(DockerSettingKeys.TlsClientCertificateSlot, DockerSettingKeys.CertificateField);
            if (String.IsNullOrWhiteSpace(certificatePem)) return null;
            string? keyPem = resource.Secrets.Get(DockerSettingKeys.TlsClientCertificateSlot, DockerSettingKeys.PrivateKeyField);
            if (String.IsNullOrWhiteSpace(keyPem)) throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The tlsClientCertificate secret needs a 'privateKey' field (PEM) next to its certificate.");
            string? caPem = resource.Secrets.Get(DockerSettingKeys.TlsClientCertificateSlot, DockerSettingKeys.CaCertificateField);

            X509Certificate2 certificate;
            try
            {
                using (X509Certificate2 ephemeral = X509Certificate2.CreateFromPem(certificatePem, keyPem))
                {
                    byte[] pfx = ephemeral.Export(X509ContentType.Pkcs12);
#if NET9_0_OR_GREATER
                    certificate = X509CertificateLoader.LoadPkcs12(pfx, null);
#else
                    certificate = new X509Certificate2(pfx);
#endif
                }
            }
            catch (CryptographicException e)
            {
                throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The tlsClientCertificate certificate and private key could not be read as PEM (" + e.Message + ").", e);
            }

            CertificateCredentials credentials = new CertificateCredentials(certificate);
            if (!tlsVerify)
            {
                credentials.ServerCertificateValidationCallback = (sender, cert, chain, errors) => true;
            }
            else if (!String.IsNullOrWhiteSpace(caPem))
            {
                X509Certificate2 ca;
                try
                {
                    ca = X509Certificate2.CreateFromPem(caPem);
                }
                catch (CryptographicException e)
                {
                    throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The tlsClientCertificate caCertificate field is not a PEM certificate (" + e.Message + ").", e);
                }
                credentials.ServerCertificateValidationCallback = CaValidator(ca);
            }
            return credentials;
        }

        /// <summary>
        /// Create a session. Performs no network I/O; the first request connects.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="options">Options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session. The caller owns it.</returns>
        /// <exception cref="ConnectorException">Thrown when the endpoint or credential cannot be used.</exception>
        public static Task<DockerSession> ConnectAsync(ConnectorResourceContext resource, DockerOptions options, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(options);
            token.ThrowIfCancellationRequested();
            Uri endpoint = ParseEndpoint(options.Endpoint);
            CertificateCredentials? credentials = LoadCredentials(resource, options.TlsVerify);
            if (options.TlsVerify && credentials == null)
                throw new ConnectorException(ConnectorErrorCodes.SecretMissing, "tlsVerify is on, so bind a Certificate secret (certificate, privateKey, caCertificate) to the 'tlsClientCertificate' slot.");

            DockerClientBuilder builder = new DockerClientBuilder()
                .WithEndpoint(endpoint)
                .WithTimeout(TimeSpan.FromMinutes(15));
            if (credentials != null) builder = builder.WithAuthProvider(credentials);
            DockerClient client;
            try
            {
                client = builder.Build();
            }
            catch (Exception e) when (e is DockerConfigurationException || e is ArgumentException || e is InvalidOperationException || e is NotSupportedException)
            {
                throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The Docker client could not be created for " + options.Endpoint + ": " + e.Message, e);
            }
            return Task.FromResult(new DockerSession(client, options.Endpoint));
        }

        #endregion

        #region Private-Methods

        private static RemoteCertificateValidationCallback CaValidator(X509Certificate2 ca)
        {
            return (sender, certificate, chain, errors) =>
            {
                if (certificate == null) return false;
                if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0) return false;
                using (X509Chain custom = new X509Chain())
                {
                    custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    custom.ChainPolicy.CustomTrustStore.Add(ca);
                    custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
#if NET9_0_OR_GREATER
                    using (X509Certificate2 leaf = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()))
#else
                    using (X509Certificate2 leaf = new X509Certificate2(certificate))
#endif
                    {
                        return custom.Build(leaf);
                    }
                }
            };
        }

        #endregion
    }
}
