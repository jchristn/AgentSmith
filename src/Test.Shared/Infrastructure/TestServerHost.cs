namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;
    using Hugo.Server;
    using Hugo.Server.Settings;
    using SyslogLogging;

    /// <summary>
    /// An in-process Hugo server on 127.0.0.1 and a random port, with an isolated database and a random master key.
    /// </summary>
    public sealed class TestServerHost : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Administrator API key the host configures.
        /// </summary>
        public static readonly string AdminApiKey = "test-admin-api-key-0123456789";

        /// <summary>
        /// Password the seeded accounts are given by <see cref="ActivateSeededUserAsync"/>.
        /// </summary>
        public static readonly string ActivatedPassword = "Changed-Password-123";

        /// <summary>
        /// Server.
        /// </summary>
        public HugoServer Server { get; }

        /// <summary>
        /// REST base URL.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Settings.
        /// </summary>
        public HugoSettings Settings { get; }

        /// <summary>
        /// Master key options the server was started with (base64 key in EnvironmentKey).
        /// </summary>
        public MasterKeyOptions KeyOptions { get; }

        /// <summary>
        /// Client authenticated with the administrator API key.
        /// </summary>
        public HttpClient AdminClient { get; }

        /// <summary>
        /// Default tenant identifier.
        /// </summary>
        public string TenantId
        {
            get
            {
                return Server.Context.DefaultTenantId;
            }
        }

        #endregion

        #region Private-Members

        private static readonly HashSet<int> _IssuedPorts = new HashSet<int>();
        private static readonly object _IssuedPortsLock = new object();

        #endregion

        #region Constructors-and-Factories

        private TestServerHost(HugoServer server, HugoSettings settings, MasterKeyOptions keyOptions)
        {
            Server = server;
            Settings = settings;
            KeyOptions = keyOptions;
            BaseUrl = "http://127.0.0.1:" + settings.Rest.Port;
            AdminClient = new HttpClient();
            AdminClient.BaseAddress = new Uri(BaseUrl);
            AdminClient.DefaultRequestHeaders.Add("x-api-key", AdminApiKey);
            AdminClient.Timeout = TimeSpan.FromSeconds(60);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start a server.
        /// </summary>
        /// <param name="provider">Database provider.</param>
        /// <param name="configure">Optional settings changes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="keyOptions">Master key options; null uses a new random key.</param>
        /// <param name="connectors">Connectors registered next to the built-ins; null registers the test echo connector.</param>
        /// <returns>Host.</returns>
        public static async Task<TestServerHost> StartAsync(DatabaseTypeEnum provider = DatabaseTypeEnum.Sqlite, Action<HugoSettings>? configure = null, CancellationToken token = default, MasterKeyOptions? keyOptions = null, System.Collections.Generic.IEnumerable<Hugo.Connectors.Abstractions.IConnector>? connectors = null)
        {
            HugoSettings settings = new HugoSettings();
            settings.NodeId = "test-server";
            settings.Rest.Hostname = "127.0.0.1";
            settings.Rest.Port = FreePort();
            settings.Database = TestEnvironment.IsolatedSettings(provider);
            settings.Logging.ConsoleLogging = Environment.GetEnvironmentVariable("HUGO_TEST_LOGS") == "true";
            settings.Telemetry.Enabled = false;
            settings.Telemetry.PrometheusEnable = false;
            settings.RequestHistory.Enabled = true;
            settings.Auth.SigningKey = "test-signing-key-0123456789";
            settings.Auth.AdminApiKey = AdminApiKey;
            settings.Auth.RateLimitPerMinute = 10000;
            settings.Secrets.RevealRateLimitPerMinute = 1000;
            settings.Secrets.MasterKeyFile = null;
            configure?.Invoke(settings);

            MasterKeyOptions options = keyOptions ?? NewKeyOptions();
            LoggingModule logging = Bootstrapper.BuildLogging(settings.Logging);
            HugoServer server = await Bootstrapper.StartAsync(settings, logging, token, options, connectors ?? new System.Collections.Generic.List<Hugo.Connectors.Abstractions.IConnector> { new Test.Shared.Connectors.Echo.EchoConnector() }).ConfigureAwait(false);
            return new TestServerHost(server, settings, options);
        }

        /// <summary>
        /// Master key options holding a new random key (version 1).
        /// </summary>
        /// <returns>Options.</returns>
        public static MasterKeyOptions NewKeyOptions()
        {
            MasterKeyOptions options = new MasterKeyOptions();
            options.EnvironmentKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            options.KeyVersion = 1;
            return options;
        }

        /// <summary>
        /// Sign in as a user and return an authenticated client.
        /// </summary>
        /// <param name="email">Email.</param>
        /// <param name="password">Password.</param>
        /// <param name="tenantId">Tenant, or null for the default tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Client with a bearer token.</returns>
        public async Task<HttpClient> UserClientAsync(string email, string password, string? tenantId = null, CancellationToken token = default)
        {
            TokenResponse issued = await LoginAsync(email, password, tenantId, token).ConfigureAwait(false);
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(BaseUrl);
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + issued.Token);
            client.Timeout = TimeSpan.FromSeconds(60);
            return client;
        }

        /// <summary>
        /// Sign in as a user.
        /// </summary>
        /// <param name="email">Email.</param>
        /// <param name="password">Password.</param>
        /// <param name="tenantId">Tenant, or null for the default tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Token response.</returns>
        /// <exception cref="TestAssertionException">Thrown when sign-in fails.</exception>
        public async Task<TokenResponse> LoginAsync(string email, string password, string? tenantId = null, CancellationToken token = default)
        {
            using (HttpClient anonymous = new HttpClient())
            {
                string body = Serializer.Serialize(new { tenantId = tenantId ?? TenantId, email, password });
                HttpResponseMessage response = await anonymous.PostAsync(BaseUrl + "/v1.0/token", new StringContent(body, Encoding.UTF8, "application/json"), token).ConfigureAwait(false);
                string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK) throw new TestAssertionException("Login failed for " + email + ": " + text);
                return Serializer.Deserialize<TokenResponse>(text) ?? throw new TestAssertionException("Empty token response.");
            }
        }

        /// <summary>
        /// Change a seeded user's password from the seed value to <see cref="ActivatedPassword"/> and return a client
        /// signed in with the new password.
        /// </summary>
        /// <param name="email">Seeded email (admin@hugo.local or anderson@hugo.local).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Client.</returns>
        public async Task<HttpClient> ActivateSeededUserAsync(string email, CancellationToken token = default)
        {
            using (HttpClient first = await UserClientAsync(email, "password", null, token).ConfigureAwait(false))
            {
                await HttpTestHelpers.SendAsync<User>(first, HttpMethod.Post, "/v1.0/api/users/me/password", new { currentPassword = "password", newPassword = ActivatedPassword }, 200, token).ConfigureAwait(false);
            }
            return await UserClientAsync(email, ActivatedPassword, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Client authenticated with credential headers.
        /// </summary>
        /// <param name="accessKey">Access key.</param>
        /// <param name="secretKey">Secret key.</param>
        /// <returns>Client.</returns>
        public HttpClient CredentialClient(string accessKey, string secretKey)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(BaseUrl);
            client.DefaultRequestHeaders.Add("x-access-key", accessKey);
            client.DefaultRequestHeaders.Add("x-secret-key", secretKey);
            client.Timeout = TimeSpan.FromSeconds(60);
            return client;
        }

        /// <summary>
        /// A free loopback TCP port not handed out before in this process.
        /// </summary>
        /// <returns>Port.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no port could be found.</exception>
        public static int FreePort()
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                lock (_IssuedPortsLock)
                {
                    if (_IssuedPorts.Add(port)) return port;
                }
            }
            throw new InvalidOperationException("Could not find a free TCP port that was not already handed out.");
        }

        /// <summary>
        /// Stop the server and release the database.
        /// </summary>
        /// <returns>Task.</returns>
        public async ValueTask DisposeAsync()
        {
            AdminClient.Dispose();
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                await Server.StopAsync(cts.Token).ConfigureAwait(false);
            }
            Server.Dispose();
            await Server.Context.Database.DisposeAsync().ConfigureAwait(false);
        }

        #endregion
    }
}
