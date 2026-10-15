namespace Test.Shared.Chat
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Test.Shared.Agent.StubLlm;
    using Test.Shared.Infrastructure;
    using Test.Shared.Integration;

    /// <summary>
    /// Helpers shared by the chat, A2A, and OpenAI-compatible suites.
    /// </summary>
    public static class ChatTestSupport
    {
        #region Public-Members

        /// <summary>
        /// Title the stub returns for title requests.
        /// </summary>
        public static readonly string StubTitle = "Checking echo for Mr Anderson";

        /// <summary>
        /// Password of users created by <see cref="UserAsync"/>.
        /// </summary>
        public static readonly string UserPassword = "Chat-Password-123";

        #endregion

        #region Public-Methods

        /// <summary>
        /// A stub LLM that answers title requests with <see cref="StubTitle"/> and everything else from its script.
        /// </summary>
        /// <returns>Stub.</returns>
        public static StubLlmServer NewStub()
        {
            StubLlmServer stub = new StubLlmServer();
            stub.Script.Responder = request => request.Body.Contains("Write a short title", StringComparison.Ordinal) ? StubLlmResponse.Message(StubTitle) : null;
            stub.Script.DefaultResponse = StubLlmResponse.Message("Good evening, Mr. Anderson. The stub has nothing scripted.");
            return stub;
        }

        /// <summary>
        /// Create a model endpoint pointing at the stub and make it the Hugo profile's inference endpoint.
        /// </summary>
        /// <param name="rig">Rig.</param>
        /// <param name="stub">Stub.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoint.</returns>
        public static async Task<ModelEndpoint> UseStubAsync(IntegrationRig rig, StubLlmServer stub, CancellationToken token)
        {
            Secret key = await rig.SecretAsync("stub-key", SecretKindEnum.ApiToken, new Dictionary<string, string> { { "token", "stub-api-key" } }, token).ConfigureAwait(false);
            ModelEndpoint endpoint = await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "stub", provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = stub.ModelName, apiKeySecretId = key.Id, maxRetries = 0, timeoutSeconds = 30 }, 201, token).ConfigureAwait(false);
            await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { inferenceEndpointId = endpoint.Id }, 200, token).ConfigureAwait(false);
            return endpoint;
        }

        /// <summary>
        /// Create a user holding one built-in role and return a signed-in client.
        /// </summary>
        /// <param name="rig">Rig.</param>
        /// <param name="email">Email.</param>
        /// <param name="role">Built-in role name, for example Operator.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Client.</returns>
        public static async Task<HttpClient> UserAsync(IntegrationRig rig, string email, string role, CancellationToken token)
        {
            User user = await HttpTestHelpers.SendAsync<User>(rig.Host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + rig.TenantId + "/users", new UserRequest { Email = email, Password = UserPassword }, 201, token).ConfigureAwait(false);
            await HttpTestHelpers.SendAsync<UserRoleAssignment>(rig.Host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + rig.TenantId + "/user-role-assignments", new AssignmentRequest { UserId = user.Id, RoleName = role }, 201, token).ConfigureAwait(false);
            rig.Context.Authorization.Invalidate();
            return await rig.Host.UserClientAsync(email, UserPassword, rig.TenantId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Start a conversation with the default profile.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="title">Title, or null.</param>
        /// <returns>Conversation.</returns>
        public static async Task<Conversation> ConversationAsync(HttpClient client, CancellationToken token, string? title = null)
        {
            return await HttpTestHelpers.SendAsync<Conversation>(client, HttpMethod.Post, "/v1.0/api/conversations", new { title = title }, 201, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST a JSON body and read the whole SSE response.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="path">Path.</param>
        /// <param name="json">JSON body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status code and events.</returns>
        public static async Task<SseResult> PostSseAsync(HttpClient client, string path, string json, CancellationToken token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, path))
            {
                request.Headers.Add("Accept", "text/event-stream");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    SseResult result = new SseResult();
                    result.StatusCode = (int)response.StatusCode;
                    result.ContentType = response.Content.Headers.ContentType?.MediaType;
                    foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers) result.Headers[header.Key.ToLowerInvariant()] = String.Join(",", header.Value);
                    if (result.ContentType != "text/event-stream")
                    {
                        result.Body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                        return result;
                    }
                    using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        SseEvent current = new SseEvent();
                        List<string> data = new List<string>();
                        while (true)
                        {
                            string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                            if (line == null) break;
                            if (line.Length == 0)
                            {
                                if (data.Count > 0 || current.Event != null)
                                {
                                    current.Data = String.Join("\n", data);
                                    result.Events.Add(current);
                                }
                                current = new SseEvent();
                                data = new List<string>();
                                continue;
                            }
                            if (line.StartsWith(":", StringComparison.Ordinal)) continue;
                            int colon = line.IndexOf(':');
                            string field = colon < 0 ? line : line.Substring(0, colon);
                            string value = colon < 0 ? String.Empty : line.Substring(colon + 1).TrimStart(' ');
                            if (field == "event") current.Event = value;
                            else if (field == "data") data.Add(value);
                            else if (field == "id") current.Id = value;
                        }
                        if (data.Count > 0)
                        {
                            current.Data = String.Join("\n", data);
                            result.Events.Add(current);
                        }
                    }
                    return result;
                }
            }
        }

        /// <summary>
        /// Wait for a pending approval in the tenant.
        /// </summary>
        /// <param name="rig">Rig.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Approval.</returns>
        /// <exception cref="TestAssertionException">Thrown when none appears within 15 seconds.</exception>
        public static async Task<Approval> PendingApprovalAsync(IntegrationRig rig, CancellationToken token)
        {
            for (int i = 0; i < 300; i++)
            {
                Approval? pending = (await rig.Context.Database.Approvals.ListPendingAsync(rig.TenantId, token).ConfigureAwait(false)).FirstOrDefault();
                if (pending != null) return pending;
                await Task.Delay(50, token).ConfigureAwait(false);
            }
            throw new TestAssertionException("No approval became pending.");
        }

        /// <summary>
        /// Wait until a condition holds.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="what">Description for the failure.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="TestAssertionException">Thrown when it does not hold within 15 seconds.</exception>
        public static async Task UntilAsync(Func<Task<bool>> condition, string what, CancellationToken token)
        {
            for (int i = 0; i < 300; i++)
            {
                if (await condition().ConfigureAwait(false)) return;
                await Task.Delay(50, token).ConfigureAwait(false);
            }
            throw new TestAssertionException("Timed out waiting for " + what + ".");
        }

        #endregion
    }
}
