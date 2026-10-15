namespace Test.Shared.Connectors.Docker
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Docker.DotNet;
    using global::Docker.DotNet.Models;

    /// <summary>
    /// Location and fixtures of the Docker-in-Docker testbed (<c>docker/testbed/docker/compose.yaml</c>). The suites run
    /// when <c>HUGO_TESTBED=1</c> and the daemon port accepts connections; otherwise every case is skipped with a reason.
    /// Override the port with <c>HUGO_TESTBED_DOCKER_PORT</c>.
    /// </summary>
    public static class DockerTestbed
    {
        #region Public-Members

        /// <summary>
        /// Fixture image.
        /// </summary>
        public static string Image { get; } = "busybox:1.37.0";

        /// <summary>
        /// Running fixture container.
        /// </summary>
        public static string WebContainer { get; } = "hugo-fixture-web";

        /// <summary>
        /// Exited fixture container (exit code 3).
        /// </summary>
        public static string StoppedContainer { get; } = "hugo-fixture-stopped";

        /// <summary>
        /// Value of FIXTURE_TOKEN in the web fixture. It must never appear in tool output unless the resource allows
        /// environment values.
        /// </summary>
        public static string SecretValue { get; } = "hugo-fixture-secret-token";

        /// <summary>
        /// Daemon port on 127.0.0.1.
        /// </summary>
        public static int Port
        {
            get
            {
                string? v = Environment.GetEnvironmentVariable("HUGO_TESTBED_DOCKER_PORT");
                return v != null && Int32.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) ? p : 28375;
            }
        }

        /// <summary>
        /// Daemon endpoint.
        /// </summary>
        public static string Endpoint
        {
            get
            {
                return "tcp://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture);
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
        /// Settings for the testbed daemon.
        /// </summary>
        /// <returns>Settings.</returns>
        public static Dictionary<string, string> Settings()
        {
            return new Dictionary<string, string> { { "endpoint", Endpoint }, { "requestTimeoutSeconds", "30" } };
        }

        /// <summary>
        /// Create and start a long-running busybox container directly through the client library.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Container ID.</returns>
        public static async Task<string> CreateVictimAsync(string name, CancellationToken token)
        {
            using (DockerClient client = Client())
            {
                CreateContainerResponse created = await client.Containers.CreateContainerAsync(new CreateContainerParameters
                {
                    Name = name,
                    Image = Image,
                    Cmd = new List<string> { "sh", "-c", "trap 'exit 0' TERM; echo victim started; while true; do sleep 1; done" },
                    Labels = new Dictionary<string, string> { { "hugo.testbed", "victim" } },
                    Env = new List<string> { "VICTIM_SECRET=victim-secret-value" }
                }, token).ConfigureAwait(false);
                await client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), token).ConfigureAwait(false);
                return created.ID;
            }
        }

        /// <summary>
        /// Remove a container if it exists.
        /// </summary>
        /// <param name="name">Container name or ID.</param>
        /// <returns>Task.</returns>
        public static async Task RemoveQuietlyAsync(string name)
        {
            using (DockerClient client = Client())
            {
                try
                {
                    await client.Containers.RemoveContainerAsync(name, new ContainerRemoveParameters { Force = true }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (DockerApiException)
                {
                }
            }
        }

        #endregion

        #region Private-Methods

        private static DockerClient Client()
        {
            return new DockerClientBuilder().WithEndpoint(new Uri(Endpoint)).Build();
        }

        private static string? Probe()
        {
            if (Environment.GetEnvironmentVariable("HUGO_TESTBED") != "1") return "Docker testbed not enabled. Start docker/testbed/docker/compose.yaml and set HUGO_TESTBED=1.";
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    if (!client.ConnectAsync("127.0.0.1", Port).Wait(TimeSpan.FromSeconds(2))) return "The Docker testbed port 127.0.0.1:" + Port + " did not accept a connection. Start docker/testbed/docker/compose.yaml.";
                }
            }
            catch (AggregateException)
            {
                return "The Docker testbed port 127.0.0.1:" + Port + " refused the connection. Start docker/testbed/docker/compose.yaml.";
            }
            catch (SocketException)
            {
                return "The Docker testbed port 127.0.0.1:" + Port + " refused the connection. Start docker/testbed/docker/compose.yaml.";
            }
            return null;
        }

        #endregion
    }
}
