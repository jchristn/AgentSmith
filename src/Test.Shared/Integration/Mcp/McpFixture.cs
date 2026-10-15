namespace Test.Shared.Integration.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Core.Serialization;
    using Hugo.Server.Settings;
    using Test.Shared.Infrastructure;

    /// <summary>
    /// The MCP suites' world: the integration rig, profile Hugo bound to echo-ro (ReadOnly), echo-rw (ReadWrite), and
    /// echo-rwa (ReadWriteWithApproval), profile Ops bound to echo-ops (ReadOnly), and a locked credential for each.
    /// </summary>
    public sealed class McpFixture : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Integration rig.
        /// </summary>
        public IntegrationRig Rig { get; }

        /// <summary>
        /// Profile Ops.
        /// </summary>
        public AgentProfile Ops { get; private set; } = new AgentProfile();

        /// <summary>
        /// Credential locked to Hugo.
        /// </summary>
        public CredentialCreateResponse HugoCredential { get; private set; } = new CredentialCreateResponse();

        /// <summary>
        /// Credential locked to Ops.
        /// </summary>
        public CredentialCreateResponse OpsCredential { get; private set; } = new CredentialCreateResponse();

        /// <summary>
        /// Server base URL.
        /// </summary>
        public string BaseUrl
        {
            get
            {
                return Rig.Host.BaseUrl;
            }
        }

        #endregion

        #region Constructors-and-Factories

        private McpFixture(IntegrationRig rig)
        {
            Rig = rig;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the world.
        /// </summary>
        /// <param name="provider">Database provider.</param>
        /// <param name="configure">Settings changes, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Fixture.</returns>
        public static async Task<McpFixture> StartAsync(DatabaseTypeEnum provider, Action<HugoSettings>? configure, CancellationToken token)
        {
            IntegrationRig rig = await IntegrationRig.StartAsync(provider, configure, token).ConfigureAwait(false);
            McpFixture fixture = new McpFixture(rig);
            try
            {
                foreach (KeyValuePair<string, AccessModeEnum> binding in new Dictionary<string, AccessModeEnum> { { "echo-ro", AccessModeEnum.ReadOnly }, { "echo-rw", AccessModeEnum.ReadWrite }, { "echo-rwa", AccessModeEnum.ReadWriteWithApproval } })
                {
                    Resource resource = await rig.ResourceAsync(binding.Key, token).ConfigureAwait(false);
                    await rig.BindAsync(rig.Hugo.Id, resource.Id, binding.Value, token).ConfigureAwait(false);
                }
                fixture.Ops = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles", new { name = "Ops" }, 201, token).ConfigureAwait(false);
                Resource ops = await rig.ResourceAsync("echo-ops", token).ConfigureAwait(false);
                await rig.BindAsync(fixture.Ops.Id, ops.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                fixture.HugoCredential = await CredentialAsync(rig, "hugo-mcp", rig.Hugo.Id, "Operator", token).ConfigureAwait(false);
                fixture.OpsCredential = await CredentialAsync(rig, "ops-mcp", fixture.Ops.Id, "Operator", token).ConfigureAwait(false);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Create a credential bound and locked to a profile.
        /// </summary>
        /// <param name="rig">Rig.</param>
        /// <param name="name">Name.</param>
        /// <param name="profileId">Profile.</param>
        /// <param name="roleName">Role granted at tenant scope, or null for none (a credential without scope assignments is denied everything).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credential with its secret.</returns>
        public static async Task<CredentialCreateResponse> CredentialAsync(IntegrationRig rig, string name, string profileId, string? roleName, CancellationToken token)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?> { { "name", name }, { "agentProfileId", profileId }, { "lockToProfile", true } };
            if (roleName != null) body["roleName"] = roleName;
            return await HttpTestHelpers.SendAsync<CredentialCreateResponse>(rig.Anderson, HttpMethod.Post, "/v1.0/api/tenants/" + rig.TenantId + "/credentials", body, 201, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Headers for a credential.
        /// </summary>
        /// <param name="credential">Credential.</param>
        /// <returns>Headers.</returns>
        public static Dictionary<string, string> Headers(CredentialCreateResponse credential)
        {
            return new Dictionary<string, string> { { "x-access-key", credential.AccessKey }, { "x-secret-key", credential.SecretKey } };
        }

        /// <summary>
        /// Bearer headers of the seeded operator, Mr. Anderson.
        /// </summary>
        /// <returns>Headers.</returns>
        public Dictionary<string, string> AndersonHeaders()
        {
            return new Dictionary<string, string> { { "Authorization", Rig.Anderson.DefaultRequestHeaders.Authorization!.ToString() } };
        }

        /// <summary>
        /// Connect a session-based client.
        /// </summary>
        /// <param name="headers">Headers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="path">Path.</param>
        /// <returns>Client.</returns>
        public Task<McpTestClient> ConnectAsync(Dictionary<string, string> headers, CancellationToken token, string path = "/mcp")
        {
            return McpTestClient.ConnectAsync(BaseUrl, path, headers, token);
        }

        /// <summary>
        /// Send one raw JSON-RPC request over HTTP.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <param name="headers">Headers.</param>
        /// <param name="body">JSON-RPC body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status, headers, and the JSON-RPC message (the data of an event stream).</returns>
        public async Task<McpRawResponse> RawAsync(string path, Dictionary<string, string> headers, object body, CancellationToken token)
        {
            using (HttpClient http = new HttpClient())
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path))
            {
                request.Content = new StringContent(Serializer.Serialize(body), Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
                foreach (KeyValuePair<string, string> header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false))
                {
                    McpRawResponse raw = new McpRawResponse();
                    raw.StatusCode = (int)response.StatusCode;
                    string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    if (String.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                    {
                        StringBuilder data = new StringBuilder();
                        using (StringReader reader = new StringReader(text))
                        {
                            string? line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (line.StartsWith("data:", StringComparison.Ordinal) && line.Length > 5 && line.Substring(5).Trim().StartsWith("{", StringComparison.Ordinal)) data.Clear().Append(line.Substring(5).Trim());
                            }
                        }
                        text = data.ToString();
                    }
                    raw.Body = text;
                    if (response.Headers.TryGetValues("WWW-Authenticate", out IEnumerable<string>? challenge)) raw.Challenge = String.Join(", ", challenge);
                    if (response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? session)) raw.SessionId = String.Join(",", session);
                    return raw;
                }
            }
        }

        /// <summary>
        /// The standard initialize request body.
        /// </summary>
        /// <param name="protocolVersion">Revision to request.</param>
        /// <returns>Body.</returns>
        public static object InitializeBody(string protocolVersion = "2025-06-18")
        {
            return new Dictionary<string, object?>
            {
                { "jsonrpc", "2.0" },
                { "id", 1 },
                { "method", "initialize" },
                { "params", new Dictionary<string, object?> { { "protocolVersion", protocolVersion }, { "capabilities", new Dictionary<string, object?>() }, { "clientInfo", new Dictionary<string, object?> { { "name", "raw" }, { "version", "1" } } } } }
            };
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        /// <returns>Task.</returns>
        public async ValueTask DisposeAsync()
        {
            await Rig.DisposeAsync().ConfigureAwait(false);
        }

        #endregion
    }
}
