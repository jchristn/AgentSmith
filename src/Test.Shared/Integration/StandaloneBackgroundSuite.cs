namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Test.Shared.Agent.StubLlm;
    using Test.Shared.Infrastructure;
    using Test.Shared.Integration.Mcp;
    using Touchstone.Core;

    /// <summary>Standalone visitor disconnects preserve background turns and capability-scoped cancellation.</summary>
    public static class StandaloneBackgroundSuite
    {
        /// <summary>Build real HTTP streaming disconnect tests.</summary><returns>Provider cases.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return TestEnvironment.Providers().Select(provider =>
            {
                string id = "standalone-background-" + provider.ToString().ToLowerInvariant();
                return new TestSuiteDescriptor(id, "Standalone background chat", new List<TestCaseDescriptor> { new TestCaseDescriptor(id, "disconnect-resume-cancel", "A disconnected stream completes in persisted history; the same visitor can cancel a later background turn", ct => DisconnectAsync(provider, ct)) });
            }).ToList();
        }

        private static async Task DisconnectAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            using (StubLlmServer stub = new StubLlmServer())
            using (HttpClient visitor = new HttpClient(new SocketsHttpHandler { MaxResponseDrainSize = 0, ResponseDrainTimeout = TimeSpan.Zero }) { BaseAddress = new Uri(rig.Host.BaseUrl) })
            {
                CredentialCreateResponse credential = await McpFixture.CredentialAsync(rig, "background-visitor", rig.Hugo.Id, "Operator", token).ConfigureAwait(false);
                ModelEndpoint endpoint = await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "background-model", provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = stub.ModelName, maxRetries = 0 }, 201, token).ConfigureAwait(false);
                AgentProfile profile = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { chatEnabled = true, chatAccessMode = "Public", chatCredentialId = credential.Credential.Id, inferenceEndpointId = endpoint.Id }, 200, token).ConfigureAwait(false);
                string path = "/v1.0/chat/" + profile.ChatLinkId;
                StandaloneChatSession session = await HttpTestHelpers.SendAsync<StandaloneChatSession>(visitor, HttpMethod.Post, path + "/conversations", new { }, 201, token).ConfigureAwait(false);
                visitor.DefaultRequestHeaders.Add("x-hugo-chat-session", session.SessionToken);
                string messages = path + "/conversations/" + session.Id + "/messages";
                string answer = String.Join(" ", Enumerable.Repeat("Background streaming persists while the visitor views another page.", 12));
                stub.Script.Responder = _ => new StubLlmResponse { Text = answer, ChunkSize = 32, ChunkDelayMs = 100 };
                await OpenThenDisconnectAsync(visitor, messages, true, token).ConfigureAwait(false);
                StandaloneChatMessages inProgress = await HttpTestHelpers.SendAsync<StandaloneChatMessages>(visitor, HttpMethod.Get, messages, null, 200, token).ConfigureAwait(false);
                Assert.True(inProgress.TurnInProgress, "Reopening history reports the disconnected turn still running");
                TestResponse overlapping = await HttpTestHelpers.SendAsync(visitor, HttpMethod.Post, messages, new { message = "Do not start an overlapping turn" }, token).ConfigureAwait(false);
                Assert.Equal(409, overlapping.StatusCode, "A second send cannot duplicate the active background turn");
                StandaloneChatMessages complete = await WaitAsync(visitor, messages, token).ConfigureAwait(false);
                Assert.True(complete.Messages.Any(m => m.Role == "assistant" && m.Content == answer), "Full assistant response is persisted after stream disconnect");
                await OpenThenDisconnectAsync(visitor, messages, false, token).ConfigureAwait(false);
                StandaloneChatCancel cancelled = await HttpTestHelpers.SendAsync<StandaloneChatCancel>(visitor, HttpMethod.Post, path + "/conversations/" + session.Id + "/cancel", new { }, 200, token).ConfigureAwait(false);
                Assert.True(cancelled.Cancelled, "The owning visitor can cancel a disconnected background turn");
                StandaloneChatMessages stopped = await WaitAsync(visitor, messages, token).ConfigureAwait(false);
                Assert.Equal(1, stopped.Messages.Count(m => m.Role == "assistant" && m.Content == answer), "Cancelled turn does not persist a duplicate complete answer");
            }
        }

        private static async Task OpenThenDisconnectAsync(HttpClient visitor, string path, bool waitForDelta, CancellationToken token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, path))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                request.Content = new StringContent("{\"message\":\"Produce a synthetic background response.\"}", Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await visitor.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                using (StreamReader reader = new StreamReader(stream))
                {
                    Assert.Equal(200, (int)response.StatusCode, "Standalone stream starts");
                    while (await reader.ReadLineAsync(token).ConfigureAwait(false) is string line)
                    {
                        if (line == "event: assistant.delta" || (!waitForDelta && line == "event: turn.started")) return;
                    }
                    throw new TestAssertionException("Streaming ended before the expected disconnect point.");
                }
            }
        }

        private static async Task<StandaloneChatMessages> WaitAsync(HttpClient visitor, string path, CancellationToken token)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                StandaloneChatMessages result = await HttpTestHelpers.SendAsync<StandaloneChatMessages>(visitor, HttpMethod.Get, path, null, 200, token).ConfigureAwait(false);
                if (!result.TurnInProgress) return result;
                await Task.Delay(100, token).ConfigureAwait(false);
            }
            throw new TestAssertionException("Standalone background turn did not settle.");
        }
    }
}
