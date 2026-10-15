namespace Hugo.Core.Secrets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Requests;
    using YamlDotNet.Core;
    using YamlDotNet.Serialization;

    /// <summary>
    /// Parses uploaded credential files for the secret form (P2.8, plan section 12.3): SSH private keys (OpenSSH and PEM,
    /// with fingerprint), Google Cloud service account JSON (project and client email), kubeconfig (contexts), and PEM
    /// certificates with an optional private key. Parsing is in memory; nothing is stored. Thread-safe (stateless).
    /// </summary>
    public static class SecretImporter
    {
        #region Public-Members

        /// <summary>
        /// Largest accepted file, in characters (1 MiB).
        /// </summary>
        public static readonly int MaxContentLength = 1048576;

        #endregion

        #region Private-Members

        private static readonly Regex _CertificateBlock = new Regex(@"-----BEGIN CERTIFICATE-----(.*?)-----END CERTIFICATE-----", RegexOptions.Singleline | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

        #endregion

        #region Public-Methods

        /// <summary>
        /// Detect the file type and describe its contents.
        /// </summary>
        /// <param name="request">Import request.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the content is empty, too large, or not a supported format.</exception>
        public static SecretImportResult Parse(SecretImportRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string content = request.Content ?? String.Empty;
            if (String.IsNullOrWhiteSpace(content)) throw new ArgumentException("The file is empty.", nameof(request));
            if (content.Length > MaxContentLength) throw new ArgumentException("The file is larger than 1 MiB.", nameof(request));

            // JSON first: a service account key embeds a PEM private key.
            string trimmed = content.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) && content.Contains("\"type\"", StringComparison.Ordinal) && content.Contains("service_account", StringComparison.Ordinal))
            {
                return ParseServiceAccount(content);
            }
            if (content.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)) return ParseCertificate(content, request.Passphrase);
            if (SshKeyInspector.ContainsPrivateKey(content)) return SshKeyInspector.Inspect(content, request.Passphrase);
            if (content.Contains("clusters", StringComparison.Ordinal) && content.Contains("contexts", StringComparison.Ordinal))
            {
                return ParseKubeconfig(content);
            }
            throw new ArgumentException("The file is not a recognized SSH private key, PEM certificate, Google Cloud service account key, or kubeconfig.", nameof(request));
        }

        /// <summary>
        /// Describe a Google Cloud service account key file.
        /// </summary>
        /// <param name="content">JSON.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentException">Thrown when the JSON is invalid or not a service account key.</exception>
        public static SecretImportResult ParseServiceAccount(string content)
        {
            GcpServiceAccountKey? key;
            try
            {
                key = JsonSerializer.Deserialize<GcpServiceAccountKey>(content);
            }
            catch (JsonException e)
            {
                throw new ArgumentException("The service account key is not valid JSON: " + e.Message, nameof(content));
            }
            if (key == null || !String.Equals(key.Type, "service_account", StringComparison.Ordinal)) throw new ArgumentException("The JSON is not a Google Cloud service account key (type must be service_account).", nameof(content));

            SecretImportResult result = new SecretImportResult();
            result.Kind = SecretKindEnum.ServiceAccountJson;
            result.Format = "GcpServiceAccount";
            result.SuggestedField = "json";
            result.Fields = new List<string> { "json" };
            result.ProjectId = key.ProjectId;
            result.ClientEmail = key.ClientEmail;
            result.PrivateKeyId = key.PrivateKeyId;
            result.HasPrivateKey = !String.IsNullOrEmpty(key.PrivateKey);
            if (!result.HasPrivateKey) result.Warnings.Add("The key file has no private_key.");
            if (String.IsNullOrEmpty(key.ClientEmail)) result.Warnings.Add("The key file has no client_email.");
            return result;
        }

        /// <summary>
        /// Describe a kubeconfig (YAML or JSON).
        /// </summary>
        /// <param name="content">Kubeconfig text.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentException">Thrown when the text is not a kubeconfig.</exception>
        public static SecretImportResult ParseKubeconfig(string content)
        {
            KubeconfigDocument? document;
            try
            {
                IDeserializer deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
                document = deserializer.Deserialize<KubeconfigDocument>(content);
            }
            catch (YamlException e)
            {
                throw new ArgumentException("The kubeconfig is not valid YAML: " + e.Message, nameof(content));
            }
            if (document == null || document.Contexts == null || document.Contexts.Count == 0) throw new ArgumentException("The kubeconfig has no contexts.", nameof(content));

            SecretImportResult result = new SecretImportResult();
            result.Kind = SecretKindEnum.Kubeconfig;
            result.Format = "Kubeconfig";
            result.SuggestedField = "kubeconfig";
            result.Fields = new List<string> { "kubeconfig" };
            result.CurrentContext = document.CurrentContext;
            foreach (KubeconfigNamedContext named in document.Contexts)
            {
                KubeconfigContextSummary summary = new KubeconfigContextSummary();
                summary.Name = named.Name ?? String.Empty;
                summary.Cluster = named.Context?.Cluster;
                summary.User = named.Context?.User;
                summary.Namespace = named.Context?.Namespace;
                summary.Server = document.Clusters?.FirstOrDefault(c => c.Name == summary.Cluster)?.Cluster?.Server;
                result.Contexts.Add(summary);
            }
            if (!String.IsNullOrEmpty(document.CurrentContext) && !result.Contexts.Any(c => c.Name == document.CurrentContext))
            {
                result.Warnings.Add("The current context " + document.CurrentContext + " is not defined.");
            }
            return result;
        }

        /// <summary>
        /// Describe a PEM certificate chain with an optional private key.
        /// </summary>
        /// <param name="content">PEM text.</param>
        /// <param name="passphrase">Optional passphrase for an encrypted key.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentException">Thrown when no certificate can be read.</exception>
        public static SecretImportResult ParseCertificate(string content, string? passphrase)
        {
            List<Match> blocks = _CertificateBlock.Matches(content).Cast<Match>().ToList();
            if (blocks.Count == 0) throw new ArgumentException("No PEM certificate was found.", nameof(content));

            SecretImportResult result = new SecretImportResult();
            result.Kind = SecretKindEnum.Certificate;
            result.Format = "X509";
            result.SuggestedField = "certificate";
            result.Fields = new List<string> { "certificate", "privateKey", "caCertificate" };
            result.CertificateCount = blocks.Count;

            byte[] der;
            try
            {
                der = Convert.FromBase64String(String.Concat(blocks[0].Groups[1].Value.Where(c => !Char.IsWhiteSpace(c))));
            }
            catch (FormatException)
            {
                throw new ArgumentException("The certificate is not valid base64.", nameof(content));
            }

            try
            {
                using (X509Certificate2 certificate = LoadCertificate(der))
                {
                    result.Subject = certificate.Subject;
                    result.Issuer = certificate.Issuer;
                    result.NotBeforeUtc = certificate.NotBefore.ToUniversalTime();
                    result.NotAfterUtc = certificate.NotAfter.ToUniversalTime();
                    result.Fingerprint = "SHA256:" + Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));
                    result.KeyType = certificate.PublicKey.Oid.FriendlyName;
                    if (result.NotAfterUtc < DateTime.UtcNow) result.Warnings.Add("The certificate expired on " + result.NotAfterUtc.Value.ToString("yyyy-MM-dd") + ".");
                }
            }
            catch (CryptographicException e)
            {
                throw new ArgumentException("The certificate could not be read: " + e.Message, nameof(content));
            }

            if (SshKeyInspector.ContainsPrivateKey(content))
            {
                result.HasPrivateKey = true;
                try
                {
                    SecretImportResult key = SshKeyInspector.Inspect(content, passphrase);
                    result.Encrypted = key.Encrypted;
                    result.KeySizeBits = key.KeySizeBits;
                    result.Warnings.AddRange(key.Warnings);
                }
                catch (ArgumentException e)
                {
                    result.Warnings.Add("The private key could not be read: " + e.Message);
                }
            }
            return result;
        }

        #endregion

        #region Private-Methods

        private static X509Certificate2 LoadCertificate(byte[] der)
        {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadCertificate(der);
#else
            return new X509Certificate2(der);
#endif
        }

        #endregion
    }
}
