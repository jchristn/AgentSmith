namespace Test.Shared.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Net.Sockets;

    /// <summary>
    /// Locations and credentials of the k3s testbed (<c>docker/testbed/kubernetes/compose.yaml</c>). The suites run when
    /// <c>HUGO_TESTBED=1</c>, the generated kubeconfig exists, and the API port accepts connections; otherwise every case
    /// is skipped with a reason. Override with <c>HUGO_TESTBED_K3S_PORT</c> and <c>HUGO_TESTBED_K3S_DIR</c>.
    /// </summary>
    public static class KubernetesTestbed
    {
        #region Public-Members

        /// <summary>
        /// Fixture namespace.
        /// </summary>
        public static string Namespace { get; } = "hugo-test";

        /// <summary>
        /// Second fixture namespace, outside the allow list in most cases.
        /// </summary>
        public static string OtherNamespace { get; } = "hugo-other";

        /// <summary>
        /// Value of the fixture Secret's password key. It must never appear in tool output.
        /// </summary>
        public static string SecretValue { get; } = "hugo-k8s-fixture-secret-value";

        /// <summary>
        /// Value of the fixture Secret's apiKey key. It must never appear in tool output.
        /// </summary>
        public static string SecretApiKey { get; } = "hugo-k8s-fixture-api-key";

        /// <summary>
        /// API server port on 127.0.0.1.
        /// </summary>
        public static int Port
        {
            get
            {
                string? v = Environment.GetEnvironmentVariable("HUGO_TESTBED_K3S_PORT");
                return v != null && Int32.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) ? p : 28443;
            }
        }

        /// <summary>
        /// API server URL.
        /// </summary>
        public static string ServerUrl
        {
            get
            {
                return "https://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture);
            }
        }

        #endregion

        #region Private-Members

        private static readonly Lazy<string?> _Unavailable = new Lazy<string?>(Probe);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Why the testbed cannot be used, or null when it can.
        /// </summary>
        /// <returns>Reason, or null.</returns>
        public static string? SkipReason()
        {
            return _Unavailable.Value;
        }

        /// <summary>
        /// Read a generated file.
        /// </summary>
        /// <param name="name">File name: <c>kubeconfig</c>, <c>reader-token</c>, or <c>ca.crt</c>.</param>
        /// <returns>Text.</returns>
        public static string Read(string name)
        {
            return File.ReadAllText(Path.Combine(Directory(), name));
        }

        /// <summary>
        /// Settings for a resource restricted to the fixture namespace.
        /// </summary>
        /// <param name="allowed">Allowed namespaces (newline-separated), or null for hugo-test.</param>
        /// <returns>Settings.</returns>
        public static Dictionary<string, string> Settings(string? allowed = null)
        {
            return new Dictionary<string, string>
            {
                { "allowedNamespaces", allowed ?? Namespace },
                { "defaultNamespace", Namespace },
                { "requestTimeoutSeconds", "30" }
            };
        }

        /// <summary>
        /// Secrets with the cluster-admin kubeconfig bound.
        /// </summary>
        /// <returns>Secrets.</returns>
        public static Dictionary<string, Dictionary<string, string>> KubeconfigSecrets()
        {
            return new Dictionary<string, Dictionary<string, string>>
            {
                { "kubeconfig", new Dictionary<string, string> { { "kubeconfig", Read("kubeconfig") } } }
            };
        }

        /// <summary>
        /// Secrets with the read-only service account token and the cluster CA bound.
        /// </summary>
        /// <returns>Secrets.</returns>
        public static Dictionary<string, Dictionary<string, string>> TokenSecrets()
        {
            return new Dictionary<string, Dictionary<string, string>>
            {
                { "serviceAccountToken", new Dictionary<string, string> { { "token", Read("reader-token").Trim() } } },
                { "certificateAuthority", new Dictionary<string, string> { { "certificate", Read("ca.crt") } } }
            };
        }

        #endregion

        #region Private-Methods

        private static string Directory()
        {
            string? env = Environment.GetEnvironmentVariable("HUGO_TESTBED_K3S_DIR");
            if (!String.IsNullOrEmpty(env) && System.IO.Directory.Exists(env)) return env;
            foreach (string start in new[] { System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                DirectoryInfo? dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "docker", "testbed", "generated", "k3s");
                    if (File.Exists(Path.Combine(candidate, "kubeconfig"))) return candidate;
                    dir = dir.Parent;
                }
            }
            throw new DirectoryNotFoundException("docker/testbed/generated/k3s/kubeconfig was not found. Start docker/testbed/kubernetes/compose.yaml.");
        }

        private static string? Probe()
        {
            if (Environment.GetEnvironmentVariable("HUGO_TESTBED") != "1") return "Kubernetes testbed not enabled. Start docker/testbed/kubernetes/compose.yaml and set HUGO_TESTBED=1.";
            try
            {
                Directory();
            }
            catch (DirectoryNotFoundException e)
            {
                return e.Message;
            }
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    if (!client.ConnectAsync("127.0.0.1", Port).Wait(TimeSpan.FromSeconds(2))) return "The k3s API port 127.0.0.1:" + Port + " did not accept a connection. Start docker/testbed/kubernetes/compose.yaml.";
                }
            }
            catch (AggregateException)
            {
                return "The k3s API port 127.0.0.1:" + Port + " refused the connection. Start docker/testbed/kubernetes/compose.yaml.";
            }
            catch (SocketException)
            {
                return "The k3s API port 127.0.0.1:" + Port + " refused the connection. Start docker/testbed/kubernetes/compose.yaml.";
            }
            return null;
        }

        #endregion
    }
}
