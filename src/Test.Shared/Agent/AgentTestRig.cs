namespace Test.Shared.Agent
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Inference;
    using Test.Shared.Agent.StubLlm;

    /// <summary>
    /// A stub LLM, a real PolyPrompt-backed gateway, an in-memory store, a runtime, a scripted catalog and
    /// executor, and a recording sink, wired together for one test. Dispose after use.
    /// </summary>
    public class AgentTestRig : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Stub LLM.
        /// </summary>
        public StubLlmServer Stub { get; }

        /// <summary>
        /// Client factory.
        /// </summary>
        public ModelClientFactory Factory { get; }

        /// <summary>
        /// Gateway.
        /// </summary>
        public ModelGateway Gateway { get; }

        /// <summary>
        /// Store.
        /// </summary>
        public InMemoryConversationStore Store { get; } = new InMemoryConversationStore();

        /// <summary>
        /// Runtime.
        /// </summary>
        public AgentRuntime Runtime { get; }

        /// <summary>
        /// Catalog.
        /// </summary>
        public ScriptedToolCatalog Catalog { get; } = new ScriptedToolCatalog();

        /// <summary>
        /// Executor.
        /// </summary>
        public ScriptedToolExecutor Executor { get; } = new ScriptedToolExecutor();

        /// <summary>
        /// Sink.
        /// </summary>
        public RecordingEventSink Sink { get; } = new RecordingEventSink();

        /// <summary>
        /// Settings used by requests built with <see cref="Request"/>.
        /// </summary>
        public AgentRuntimeSettings Settings { get; } = new AgentRuntimeSettings();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="gatewaySettings">Gateway settings; null uses fast test defaults.</param>
        public AgentTestRig(ModelGatewaySettings? gatewaySettings = null)
        {
            Stub = new StubLlmServer();
            Factory = new ModelClientFactory();
            Gateway = new ModelGateway(Factory, gatewaySettings ?? new ModelGatewaySettings { RetryBaseDelayMs = 10, QueueTimeoutSeconds = 10 });
            Runtime = new AgentRuntime(Gateway, Store, Settings);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a request against the stub.
        /// </summary>
        /// <param name="message">User message.</param>
        /// <param name="conversationId">Conversation ID.</param>
        /// <returns>Request.</returns>
        public AgentTurnRequest Request(string message, string conversationId = "cnv_test")
        {
            return new AgentTurnRequest
            {
                TenantId = "ten_test",
                ConversationId = conversationId,
                PrincipalId = "usr_anderson",
                ProfileId = "prf_hugo",
                Surface = "chat",
                SystemPrompt = "You are Agent Smith. Address the operator as Mr. Anderson.",
                UserMessage = message,
                InferenceEndpoint = Stub.InferenceEndpoint(),
                ToolCatalog = Catalog,
                ToolExecutor = Executor,
                EventSink = Sink,
                Settings = Settings
            };
        }

        /// <summary>
        /// Run a turn.
        /// </summary>
        /// <param name="message">User message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        public Task<AgentTurnResult> RunAsync(string message, CancellationToken token)
        {
            return Runtime.RunTurnAsync(Request(message), token);
        }

        /// <summary>
        /// Dispose the stub and the factory.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            Stub.Dispose();
            Factory.Dispose();
        }

        #endregion
    }
}
