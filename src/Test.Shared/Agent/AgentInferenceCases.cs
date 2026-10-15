namespace Test.Shared.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Inference;
    using PolyPrompt.Models;
    using Test.Shared.Agent.StubLlm;
    using Touchstone.Core;

    /// <summary>
    /// Model gateway cases against the stub LLM through real PolyPrompt OpenAI-compatible clients.
    /// </summary>
    public static class AgentInferenceCases
    {
        private const string Suite = "agent.inference";

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(Suite, "streaming-tool-chat", "Gateway streams text and assembles tool calls", async ct =>
            {
                using (StubLlmServer stub = new StubLlmServer())
                using (ModelClientFactory factory = new ModelClientFactory())
                {
                    ModelGateway gateway = new ModelGateway(factory);
                    StubLlmResponse scripted = StubLlmResponse.ToolCallList(new StubToolCall("sql_query", "{\"resource\":\"prod-db\",\"query\":\"select 1\"}"), new StubToolCall("ssh_exec", "{\"resource\":\"web-01\",\"command\":\"uptime\"}"));
                    scripted.Text = "Checking both.";
                    stub.Script.Then(scripted);
                    StringBuilder streamed = new StringBuilder();
                    ModelChatCall call = Call(stub.InferenceEndpoint(), "why is checkout slow?");
                    call.Tools = new List<ToolDefinition> { Tool("sql_query"), Tool("ssh_exec") };
                    call.OnChunk = (chunk, t) =>
                    {
                        if (!String.IsNullOrEmpty(chunk.Text)) streamed.Append(chunk.Text);
                        return Task.CompletedTask;
                    };
                    ModelChatResult r = await gateway.ChatAsync(call, ct).ConfigureAwait(false);
                    AgentAssert.Equal("Checking both.", streamed.ToString(), "streamed text");
                    AgentAssert.Equal(2, r.ToolCalls.Count, "tool calls");
                    AgentAssert.Equal("sql_query", r.ToolCalls[0].Name, "first call");
                    AgentAssert.Equal("{\"resource\":\"web-01\",\"command\":\"uptime\"}", r.ToolCalls[1].ArgumentsJson, "arguments reassembled");
                    AgentAssert.True(r.InputTokens > 0 && r.OutputTokens > 0 && !r.TokensEstimated, "usage reported");
                    AgentAssert.True(r.TimeToFirstTokenMs >= 0, "ttft");
                    AgentAssert.True(stub.ChatRequests[0].Stream && stub.ChatRequests[0].ToolChoice == "auto", "streaming request with auto tool choice");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "non-streaming", "Gateway uses non-streaming tool chat when the endpoint cannot stream", async ct =>
            {
                using (StubLlmServer stub = new StubLlmServer())
                using (ModelClientFactory factory = new ModelClientFactory())
                {
                    ModelGateway gateway = new ModelGateway(factory);
                    stub.Script.Then(StubLlmResponse.Message("pong"));
                    ModelEndpointConfig endpoint = stub.InferenceEndpoint();
                    endpoint.SupportsStreaming = false;
                    int chunks = 0;
                    ModelChatCall call = Call(endpoint, "ping");
                    call.OnChunk = (chunk, t) =>
                    {
                        chunks++;
                        return Task.CompletedTask;
                    };
                    ModelChatResult r = await gateway.ChatAsync(call, ct).ConfigureAwait(false);
                    AgentAssert.Equal("pong", r.Text, "text");
                    AgentAssert.Equal(1, chunks, "one synthetic chunk");
                    AgentAssert.True(!stub.ChatRequests[0].Stream, "not streamed");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "retry-honors-retry-after", "Gateway retries a 429 after the advertised Retry-After", async ct =>
            {
                using (StubLlmServer stub = new StubLlmServer())
                using (ModelClientFactory factory = new ModelClientFactory())
                {
                    ModelGateway gateway = new ModelGateway(factory, new ModelGatewaySettings { RetryBaseDelayMs = 10 });
                    stub.Script.Then(StubLlmResponse.Error(429, 1), StubLlmResponse.Message("after the wait"));
                    ModelEndpointConfig endpoint = stub.InferenceEndpoint();
                    endpoint.MaxRetries = 2;
                    Stopwatch sw = Stopwatch.StartNew();
                    ModelChatResult r = await gateway.ChatAsync(Call(endpoint, "hello"), ct).ConfigureAwait(false);
                    sw.Stop();
                    AgentAssert.Equal("after the wait", r.Text, "answer");
                    AgentAssert.Equal(2, r.Attempts, "attempts");
                    AgentAssert.True(sw.ElapsedMilliseconds >= 900, "waited for Retry-After: " + sw.ElapsedMilliseconds + " ms");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "retry-5xx-then-give-up-on-4xx", "Gateway retries 5xx with backoff and does not retry 400", async ct =>
            {
                using (StubLlmServer stub = new StubLlmServer())
                using (ModelClientFactory factory = new ModelClientFactory())
                {
                    ModelGateway gateway = new ModelGateway(factory, new ModelGatewaySettings { RetryBaseDelayMs = 5 });
                    ModelEndpointConfig endpoint = stub.InferenceEndpoint();
                    endpoint.MaxRetries = 3;
                    stub.Script.Then(StubLlmResponse.Error(500), StubLlmResponse.Error(502), StubLlmResponse.Message("third time"));
                    ModelChatResult r = await gateway.ChatAsync(Call(endpoint, "x"), ct).ConfigureAwait(false);
                    AgentAssert.Equal(3, r.Attempts, "two retries");
                    stub.Script.Then(StubLlmResponse.Error(400), StubLlmResponse.Message("never"));
                    ModelEndpointException e = await AgentAssert.ThrowsAsync<ModelEndpointException>(() => gateway.ChatAsync(Call(endpoint, "x"), ct), "400 fails").ConfigureAwait(false);
                    AgentAssert.True(e.Code == "http_error" && e.StatusCode == 400 && !e.Retryable, "classified: " + e.Code);
                    AgentAssert.Equal(4, stub.ChatRequests.Count, "no retry after 400");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "fallback-endpoint", "Gateway uses the fallback endpoint when the primary fails", async ct =>
            {
                using (StubLlmServer primary = new StubLlmServer())
                using (StubLlmServer fallback = new StubLlmServer())
                using (ModelClientFactory factory = new ModelClientFactory())
                {
                    ModelGateway gateway = new ModelGateway(factory, new ModelGatewaySettings { RetryBaseDelayMs = 5 });
                    primary.Script.Responder = r => StubLlmResponse.Error(503);
                    fallback.Script.Then(StubLlmResponse.Message("from fallback"));
                    ModelEndpointConfig p = primary.InferenceEndpoint();
                    p.MaxRetries = 1;
                    ModelChatCall call = Call(p, "hello");
                    call.Fallback = fallback.InferenceEndpoint();
                    ModelChatResult r = await gateway.ChatAsync(call, ct).ConfigureAwait(false);
                    AgentAssert.Equal("from fallback", r.Text, "answer");
                    AgentAssert.True(r.UsedFallback, "fallback flagged");
                    AgentAssert.Equal(3, r.Attempts, "two primary attempts plus fallback");
                    AgentAssert.Equal(fallback.InferenceEndpoint().Key, r.EndpointKey, "endpoint key");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "circuit-breaker-opens", "Gateway opens the circuit after consecutive failures and fails fast", async ct =>
            {
                using (StubLlmServer primary = new StubLlmServer())
                using (StubLlmServer fallback = new StubLlmServer())
                using (ModelClientFactory factory = new ModelClientFactory())
                {
                    ModelGateway gateway = new ModelGateway(factory, new ModelGatewaySettings { CircuitFailureThreshold = 2, CircuitOpenSeconds = 30, RetryBaseDelayMs = 5 });
                    primary.Script.Responder = r => StubLlmResponse.Error(500);
                    ModelEndpointConfig p = primary.InferenceEndpoint();
                    for (int i = 0; i < 2; i++)
                    {
                        ModelEndpointException e = await AgentAssert.ThrowsAsync<ModelEndpointException>(() => gateway.ChatAsync(Call(p, "x"), ct), "failure " + i).ConfigureAwait(false);
                        AgentAssert.Equal("http_error", e.Code, "http error " + i);
                    }
                    AgentAssert.True(gateway.Breaker.IsOpen(p.Key), "circuit open");
                    ModelEndpointException open = await AgentAssert.ThrowsAsync<ModelEndpointException>(() => gateway.ChatAsync(Call(p, "x"), ct), "fails fast").ConfigureAwait(false);
                    AgentAssert.Equal("circuit_open", open.Code, "circuit_open");
                    AgentAssert.Equal(2, primary.ChatRequests.Count, "no request while open");

                    fallback.Script.Then(StubLlmResponse.Message("fallback while open"));
                    ModelChatCall call = Call(p, "x");
                    call.Fallback = fallback.InferenceEndpoint();
                    ModelChatResult r = await gateway.ChatAsync(call, ct).ConfigureAwait(false);
                    AgentAssert.True(r.UsedFallback && r.Text == "fallback while open", "fallback serves while open");
                    AgentAssert.Equal(2, primary.ChatRequests.Count, "primary skipped");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "transport-failure", "Gateway reports an unreachable endpoint as a retryable transport error", async ct =>
            {
                using (ModelClientFactory factory = new ModelClientFactory())
                {
                    ModelGateway gateway = new ModelGateway(factory, new ModelGatewaySettings { RetryBaseDelayMs = 5 });
                    ModelEndpointConfig dead = new ModelEndpointConfig { Id = "dead", Provider = ModelProviderEnum.OpenAICompatible, BaseUrl = "http://127.0.0.1:9", Model = "m", MaxRetries = 1, TimeoutSeconds = 5 };
                    ModelEndpointException e = await AgentAssert.ThrowsAsync<ModelEndpointException>(() => gateway.ChatAsync(Call(dead, "x"), ct), "unreachable").ConfigureAwait(false);
                    AgentAssert.True(e.Retryable && (e.Code == "transport_error" || e.Code == "timeout"), "classified: " + e.Code);
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "embeddings", "Gateway embeds a batch through the stub's hashing embedder", async ct =>
            {
                using (StubLlmServer stub = new StubLlmServer())
                using (ModelClientFactory factory = new ModelClientFactory())
                {
                    ModelGateway gateway = new ModelGateway(factory);
                    ModelEmbedResult r = await gateway.EmbedAsync(new ModelEmbedCall { Endpoint = stub.EmbeddingEndpoint(), Inputs = new List<string> { "alpha beta", "gamma" } }, ct).ConfigureAwait(false);
                    AgentAssert.Equal(2, r.Vectors.Count, "vectors");
                    AgentAssert.Equal(256, r.Dimensions, "dimensions");
                    AgentAssert.True(r.Vectors[0].SequenceEqual(stub.Embedder.Embed("alpha beta")), "deterministic vector");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "validate-endpoints", "Validation reports latency, a reply preview, and embedding dimensions", async ct =>
            {
                using (StubLlmServer stub = new StubLlmServer())
                using (ModelClientFactory factory = new ModelClientFactory())
                {
                    ModelGateway gateway = new ModelGateway(factory);
                    stub.Script.Then(StubLlmResponse.Message("pong"));
                    ModelValidationResult inference = await gateway.ValidateAsync(stub.InferenceEndpoint(), ct).ConfigureAwait(false);
                    AgentAssert.True(inference.Success && inference.ResponsePreview == "pong" && inference.LatencyMs >= 0, "inference valid: " + inference.Error);
                    ModelValidationResult embedding = await gateway.ValidateAsync(stub.EmbeddingEndpoint(), ct).ConfigureAwait(false);
                    AgentAssert.True(embedding.Success && embedding.Dimensions == 256, "embedding valid: " + embedding.Error);
                    ModelValidationResult misconfigured = await gateway.ValidateAsync(new ModelEndpointConfig { Provider = ModelProviderEnum.OpenAICompatible, Model = "m" }, ct).ConfigureAwait(false);
                    AgentAssert.True(!misconfigured.Success && misconfigured.Problems.Count > 0, "misconfigured");
                    stub.Script.Then(StubLlmResponse.Error(401));
                    ModelValidationResult rejected = await gateway.ValidateAsync(stub.InferenceEndpoint(), ct).ConfigureAwait(false);
                    AgentAssert.True(!rejected.Success && rejected.StatusCode == 401 && rejected.Error != null, "401 reported");
                    AgentAssert.True(!rejected.Error!.Contains("stub-key", StringComparison.Ordinal), "API key not echoed");
                }
            }));

            return cases;
        }

        private static ModelChatCall Call(ModelEndpointConfig endpoint, string user)
        {
            return new ModelChatCall
            {
                Endpoint = endpoint,
                Messages = new List<ChatMessage> { ChatMessage.System("You are a test."), ChatMessage.User(user) }
            };
        }

        private static ToolDefinition Tool(string name)
        {
            return ToolDefinition.Function(name, "Test tool " + name, new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object> { { "resource", new Dictionary<string, object> { { "type", "string" } } } } }
            });
        }
    }
}
