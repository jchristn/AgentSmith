namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using k8s;
    using k8s.KubeConfigModels;

    /// <summary>
    /// Parses a kubeconfig and refuses anything that would make the Hugo server run programs or read its own files:
    /// <c>exec</c> credential plugins, <c>auth-provider</c> plugins, and file references (<c>certificate-authority</c>,
    /// <c>client-certificate</c>, <c>client-key</c>). Inline data fields and tokens are accepted. Thread-safe.
    /// </summary>
    public static class KubeconfigInspector
    {
        #region Public-Methods

        /// <summary>
        /// Inspect kubeconfig text.
        /// </summary>
        /// <param name="text">Kubeconfig YAML or JSON.</param>
        /// <param name="context">Context to select, or null for current-context.</param>
        /// <returns>Inspection; check <see cref="KubeconfigInspection.IsValid"/>.</returns>
        public static KubeconfigInspection Inspect(string? text, string? context)
        {
            KubeconfigInspection result = new KubeconfigInspection();
            if (String.IsNullOrWhiteSpace(text))
            {
                result.Errors.Add("The kubeconfig is empty.");
                return result;
            }

            K8SConfiguration? config;
            try
            {
                config = KubernetesYaml.Deserialize<K8SConfiguration>(text, false);
            }
            catch (Exception e) when (e is YamlDotNet.Core.YamlException || e is ArgumentException || e is InvalidOperationException || e is FormatException)
            {
                result.Errors.Add("The kubeconfig is not valid YAML (" + FirstLine(e.Message) + ").");
                return result;
            }
            if (config == null)
            {
                result.Errors.Add("The kubeconfig is empty.");
                return result;
            }
            result.Configuration = config;

            List<Context> contexts = (config.Contexts ?? Enumerable.Empty<Context>()).Where(c => c != null).ToList();
            result.Contexts = contexts.Select(c => c.Name ?? String.Empty).Where(n => n.Length > 0).ToList();
            if (contexts.Count == 0)
            {
                result.Errors.Add("The kubeconfig has no contexts.");
                return result;
            }

            string? wanted = String.IsNullOrWhiteSpace(context) ? config.CurrentContext : context.Trim();
            if (String.IsNullOrWhiteSpace(wanted))
            {
                if (contexts.Count == 1) wanted = contexts[0].Name;
                else
                {
                    result.Errors.Add("The kubeconfig has no current-context. Set the 'context' setting to one of: " + String.Join(", ", result.Contexts) + ".");
                    return result;
                }
            }

            Context? selected = contexts.FirstOrDefault(c => String.Equals(c.Name, wanted, StringComparison.Ordinal));
            if (selected == null || selected.ContextDetails == null)
            {
                result.Errors.Add("Context '" + wanted + "' is not in the kubeconfig. Available contexts: " + String.Join(", ", result.Contexts) + ".");
                return result;
            }
            result.ContextName = selected.Name;
            result.Namespace = selected.ContextDetails.Namespace;

            Cluster? cluster = (config.Clusters ?? Enumerable.Empty<Cluster>()).FirstOrDefault(c => c != null && String.Equals(c.Name, selected.ContextDetails.Cluster, StringComparison.Ordinal));
            if (cluster == null || cluster.ClusterEndpoint == null)
            {
                result.Errors.Add("Context '" + wanted + "' refers to cluster '" + selected.ContextDetails.Cluster + "', which is not in the kubeconfig.");
            }
            else
            {
                result.Server = cluster.ClusterEndpoint.Server;
                if (String.IsNullOrWhiteSpace(cluster.ClusterEndpoint.Server)) result.Errors.Add("Cluster '" + cluster.Name + "' has no server URL.");
                if (!String.IsNullOrEmpty(cluster.ClusterEndpoint.CertificateAuthority))
                    result.Errors.Add("Cluster '" + cluster.Name + "' references a certificate-authority file. Hugo does not read files from its own disk; embed it as certificate-authority-data.");
            }

            if (!String.IsNullOrEmpty(selected.ContextDetails.User))
            {
                User? user = (config.Users ?? Enumerable.Empty<User>()).FirstOrDefault(u => u != null && String.Equals(u.Name, selected.ContextDetails.User, StringComparison.Ordinal));
                if (user == null)
                {
                    result.Errors.Add("Context '" + wanted + "' refers to user '" + selected.ContextDetails.User + "', which is not in the kubeconfig.");
                }
                else if (user.UserCredentials != null)
                {
                    UserCredentials u = user.UserCredentials;
                    if (u.ExternalExecution != null)
                        result.Errors.Add("User '" + user.Name + "' uses an exec credential plugin, which would run a program on the Hugo server. Use a token or client certificate kubeconfig (for example a service account token).");
                    if (u.AuthProvider != null)
                        result.Errors.Add("User '" + user.Name + "' uses an auth-provider plugin, which Hugo does not support. Use a token or client certificate kubeconfig.");
                    if (!String.IsNullOrEmpty(u.ClientCertificate) || !String.IsNullOrEmpty(u.ClientKey))
                        result.Errors.Add("User '" + user.Name + "' references client certificate or key files. Embed them as client-certificate-data and client-key-data.");
                }
            }
            return result;
        }

        #endregion

        #region Private-Methods

        private static string FirstLine(string message)
        {
            string m = message ?? String.Empty;
            int nl = m.IndexOf('\n');
            if (nl >= 0) m = m.Substring(0, nl);
            return m.Length > 200 ? m.Substring(0, 197) + "..." : m;
        }

        #endregion
    }
}
