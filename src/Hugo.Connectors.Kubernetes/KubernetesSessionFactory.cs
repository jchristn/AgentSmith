namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using k8s;

    /// <summary>
    /// Builds KubernetesClient configurations and sessions from a resource's settings and bound secrets. Supports a
    /// kubeconfig (inline credentials only, see <see cref="KubeconfigInspector"/>) or a service account token with a
    /// server URL and an optional cluster CA. Thread-safe.
    /// </summary>
    public static class KubernetesSessionFactory
    {
        #region Public-Methods

        /// <summary>
        /// Build a client configuration. Performs no network I/O.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="options">Options.</param>
        /// <returns>Configuration.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>secret_missing</c>, <c>invalid_setting</c>, or <c>invalid_argument</c> when the credential cannot be used.</exception>
        public static KubernetesClientConfiguration BuildConfiguration(ConnectorResourceContext resource, KubernetesOptions options)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(options);
            KubernetesClientConfiguration config;
            string? kubeconfig = resource.Secrets.Get(KubernetesSettingKeys.KubeconfigSlot, KubernetesSettingKeys.KubeconfigField);
            string? token = resource.Secrets.Get(KubernetesSettingKeys.ServiceAccountTokenSlot, KubernetesSettingKeys.TokenField);

            if (!String.IsNullOrWhiteSpace(kubeconfig))
            {
                KubeconfigInspection inspection = KubeconfigInspector.Inspect(kubeconfig, options.Context);
                if (!inspection.IsValid || inspection.Configuration == null)
                    throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The bound kubeconfig cannot be used: " + inspection.ErrorText());
                try
                {
                    config = KubernetesClientConfiguration.BuildConfigFromConfigObject(inspection.Configuration, inspection.ContextName, options.ServerUrl);
                }
                catch (Exception e) when (e is k8s.Exceptions.KubeConfigException || e is FormatException || e is CryptographicException || e is ArgumentException)
                {
                    throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The bound kubeconfig cannot be used: " + Shorten(e.Message, 300), e);
                }
            }
            else if (!String.IsNullOrWhiteSpace(token))
            {
                if (String.IsNullOrWhiteSpace(options.ServerUrl))
                    throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "Token authentication needs the serverUrl setting (for example https://10.0.0.1:6443).");
                config = new KubernetesClientConfiguration();
                config.Host = options.ServerUrl;
                config.AccessToken = token.Trim();
                X509Certificate2Collection? ca = LoadCertificateAuthority(resource);
                if (ca != null) config.SslCaCerts = ca;
            }
            else
            {
                throw new ConnectorException(ConnectorErrorCodes.SecretMissing, "Bind a Kubeconfig secret to the 'kubeconfig' slot, or a BearerToken (or ApiToken) secret to the 'serviceAccountToken' slot together with the serverUrl setting.");
            }

            if (options.InsecureSkipTlsVerify) config.SkipTlsVerify = true;
            if (String.IsNullOrWhiteSpace(config.Host)) throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "No API server URL is configured.");
            config.HttpClientTimeout = TimeSpan.FromMinutes(15);
            config.UserAgent = "Hugo";
            return config;
        }

        /// <summary>
        /// Create a session. Performs no network I/O; the first request connects.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="options">Options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session. The caller owns it.</returns>
        /// <exception cref="ConnectorException">Thrown when the credential cannot be used.</exception>
        public static Task<KubernetesSession> ConnectAsync(ConnectorResourceContext resource, KubernetesOptions options, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            KubernetesClientConfiguration config = BuildConfiguration(resource, options);
            Kubernetes client;
            try
            {
                client = new Kubernetes(config);
            }
            catch (Exception e) when (e is k8s.Exceptions.KubeConfigException || e is FormatException || e is CryptographicException || e is ArgumentException || e is UriFormatException)
            {
                throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The Kubernetes client could not be created: " + Shorten(e.Message, 300), e);
            }
            KubernetesSession session = new KubernetesSession(client, config.Host);
            session.RequestTimeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            return Task.FromResult(session);
        }

        /// <summary>
        /// Load the cluster CA bound to the <c>certificateAuthority</c> slot (its <c>certificate</c> field, or its
        /// <c>caCertificate</c> field), or null when none is bound.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <returns>Certificates, or null.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_setting</c> when the PEM cannot be read.</exception>
        public static X509Certificate2Collection? LoadCertificateAuthority(ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            string? pem = resource.Secrets.Get(KubernetesSettingKeys.CertificateAuthoritySlot, KubernetesSettingKeys.CertificateField);
            if (String.IsNullOrWhiteSpace(pem)) pem = resource.Secrets.Get(KubernetesSettingKeys.CertificateAuthoritySlot, KubernetesSettingKeys.CaCertificateField);
            if (String.IsNullOrWhiteSpace(pem)) return null;
            try
            {
                X509Certificate2Collection collection = new X509Certificate2Collection();
                collection.ImportFromPem(pem);
                if (collection.Count == 0) throw new CryptographicException("no certificates found");
                return collection;
            }
            catch (CryptographicException e)
            {
                throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The certificate bound to 'certificateAuthority' is not a PEM certificate (" + Shorten(e.Message, 120) + ").", e);
            }
        }

        #endregion

        #region Private-Methods

        private static string Shorten(string value, int max)
        {
            string v = (value ?? String.Empty).Replace('\n', ' ').Replace('\r', ' ');
            return v.Length > max ? v.Substring(0, max - 3) + "..." : v;
        }

        #endregion
    }
}
