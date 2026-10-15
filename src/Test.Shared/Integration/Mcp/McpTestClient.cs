namespace Test.Shared.Integration.Mcp
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Serialization;
    using Test.Shared.Infrastructure;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// A real Voltaic MCP client (session-based Streamable HTTP or the stateless 2026-07-28 revision) with typed
    /// helpers for the MCP suites.
    /// </summary>
    public sealed class McpTestClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The Voltaic client.
        /// </summary>
        public McpHttpClient Client { get; }

        /// <summary>
        /// Notifications received (progress and others).
        /// </summary>
        public ConcurrentQueue<JsonRpcRequest> Notifications { get; } = new ConcurrentQueue<JsonRpcRequest>();

        /// <summary>
        /// Whether the client uses the stateless revision.
        /// </summary>
        public bool Stateless { get; }

        #endregion

        #region Constructors-and-Factories

        private McpTestClient(McpHttpClient client, bool stateless)
        {
            Client = client;
            Stateless = stateless;
            Client.NotificationReceived += (_, notification) => Notifications.Enqueue(notification);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Connect with a session (initialize handshake).
        /// </summary>
        /// <param name="baseUrl">Server base URL.</param>
        /// <param name="path">MCP path (/mcp or /mcp/{profile}).</param>
        /// <param name="headers">Authentication and other headers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Client.</returns>
        /// <exception cref="TestAssertionException">Thrown when the handshake fails.</exception>
        public static async Task<McpTestClient> ConnectAsync(string baseUrl, string path, Dictionary<string, string> headers, CancellationToken token)
        {
            McpHttpClient client = Build(headers);
            if (!await client.ConnectStreamableAsync(baseUrl, path, token).ConfigureAwait(false))
            {
                client.Dispose();
                throw new TestAssertionException("The MCP handshake on " + path + " failed.");
            }
            return new McpTestClient(client, false);
        }

        /// <summary>
        /// Connect statelessly (2026-07-28, server/discover; the revision Claude Code uses).
        /// </summary>
        /// <param name="baseUrl">Server base URL.</param>
        /// <param name="path">MCP path.</param>
        /// <param name="headers">Headers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Client.</returns>
        /// <exception cref="TestAssertionException">Thrown when discovery fails.</exception>
        public static async Task<McpTestClient> ConnectStatelessAsync(string baseUrl, string path, Dictionary<string, string> headers, CancellationToken token)
        {
            McpHttpClient client = Build(headers);
            if (!await client.ConnectStatelessAsync(baseUrl, path, null, true, token).ConfigureAwait(false))
            {
                client.Dispose();
                throw new TestAssertionException("The stateless MCP connection on " + path + " failed.");
            }
            return new McpTestClient(client, true);
        }

        /// <summary>
        /// Call a method and return the raw response.
        /// </summary>
        /// <param name="method">Method.</param>
        /// <param name="parameters">Params, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response.</returns>
        public async Task<JsonRpcResponse> RawAsync(string method, Dictionary<string, object?>? parameters, CancellationToken token)
        {
            if (Stateless) return await Client.SendStatelessAsync(method, parameters, null, token).ConfigureAwait(false);
            return await Client.CallAsync(method, parameters, 0, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Call a method and read its result.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="method">Method.</param>
        /// <param name="parameters">Params, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="TestAssertionException">Thrown on a JSON-RPC error.</exception>
        public async Task<T> CallAsync<T>(string method, Dictionary<string, object?>? parameters, CancellationToken token) where T : class
        {
            JsonRpcResponse response = await RawAsync(method, parameters, token).ConfigureAwait(false);
            if (response.Error != null) throw new TestAssertionException(method + " failed: " + response.Error.Code + " " + response.Error.Message);
            return Serializer.Deserialize<T>(Serializer.Serialize(response.Result)) ?? throw new TestAssertionException(method + " returned no result.");
        }

        /// <summary>
        /// List tools.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tools.</returns>
        public Task<McpTestToolList> ListToolsAsync(CancellationToken token)
        {
            return CallAsync<McpTestToolList>("tools/list", null, token);
        }

        /// <summary>
        /// Call a tool.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="progressToken">Progress token to send, or null.</param>
        /// <returns>Result.</returns>
        public Task<McpTestToolResult> CallToolAsync(string name, object? arguments, CancellationToken token, string? progressToken = null)
        {
            Dictionary<string, object?> parameters = new Dictionary<string, object?>();
            parameters["name"] = name;
            parameters["arguments"] = arguments ?? new Dictionary<string, object?>();
            if (progressToken != null) parameters["_meta"] = new Dictionary<string, object?> { { "progressToken", progressToken } };
            return CallAsync<McpTestToolResult>("tools/call", parameters, token);
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Client.Dispose();
        }

        #endregion

        #region Private-Methods

        private static McpHttpClient Build(Dictionary<string, string> headers)
        {
            McpHttpClient client = new McpHttpClient();
            client.ClientName = "hugo-tests";
            client.ClientVersion = "1.0.0";
            client.RequestTimeoutMs = 120000;
            client.AutoReconnectSse = false;
            foreach (KeyValuePair<string, string> header in headers) client.SetRequestHeader(header.Key, header.Value);
            return client;
        }

        #endregion
    }
}
