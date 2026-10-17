namespace Hugo.Server.Modules
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Inference;
    using Hugo.Core.Instructions;
    using Hugo.Core.Profiles;
    using Hugo.Core.Prompts;
    using Hugo.Core.Security;
    using Hugo.Server.Routes;
    using Hugo.Server.Services;

    /// <summary>
    /// Hugo's own loop and the surfaces that run it (Phase 6 runtime hosting, Phase 7 A2A and OpenAI-compatible):
    /// conversations and dashboard chat, analytics, delegation (<see cref="IAgentDelegationService"/>), A2A, and the
    /// OpenAI-compatible endpoint. Runs after the integration module, whose services it uses.
    /// </summary>
    public class AgentModule : IServerModule
    {
        #region Public-Members

        /// <summary>
        /// Module name.
        /// </summary>
        public string Name
        {
            get
            {
                return "AgentModule";
            }
        }

        #endregion

        #region Private-Members

        private AgentTurnHost? _Host = null;
        private A2ATaskService? _Tasks = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the agent services and register them on the context.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public Task InitializeAsync(ServerContext context, CancellationToken token)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EventHub? hub = context.TryGet<EventHub>();
            _Host = new AgentTurnHost(context, context.Get<IModelGateway>(), context.Get<ModelEndpointService>(), context.Get<InstructionCompiler>(), context.Get<ToolCatalogService>(), context.Get<ToolInvocationService>(), hub);
            context.Register(_Host);
            ConversationService conversations = new ConversationService(context.Database, _Host.Turns);
            context.Register(conversations);
            context.Register(new StandaloneChatService(context, _Host));
            AgentDelegationService delegation = new AgentDelegationService(context, _Host, context.Get<ProfileResolver>(), conversations);
            context.Register(delegation);
            context.Register<IAgentDelegationService>(delegation);
            context.AgentDelegation = delegation;
            context.Register(new AnalyticsService(context.Database));
            context.Register(new AgentCardService(context.Get<ProfileBindingResolver>(), context.Get<PromptTemplateService>()));
            _Tasks = new A2ATaskService(context, _Host, conversations, context.Get<ApprovalService>());
            context.Register(_Tasks);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Register the conversation, analytics, delegation, A2A, and OpenAI-compatible routes.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="routes">Registrar.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public void RegisterRoutes(ServerContext context, RouteRegistrar routes)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            ProfileResolver resolver = context.Get<ProfileResolver>();
            ConversationService conversations = context.Get<ConversationService>();
            new ConversationRoutes(context, conversations, _Host!, resolver).Register(routes);
            new StandaloneChatRoutes(context, context.Get<StandaloneChatService>()).Register(routes);
            new AnalyticsRoutes(context.Get<AnalyticsService>()).Register(routes);
            new AgentDelegationRoutes(context.Get<AgentDelegationService>()).Register(routes);
            new A2ARoutes(context, _Tasks!, context.Get<AgentCardService>()).Register(routes);
            new OpenAIRoutes(context, _Host!, conversations, resolver).Register(routes);
        }

        /// <summary>
        /// Settle A2A tasks a previous process left unfinished.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public async Task StartAsync(ServerContext context, CancellationToken token)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            try
            {
                int settled = await _Tasks!.RecoverAsync(token).ConfigureAwait(false);
                if (settled > 0) context.Logging.Info("[Agent] marked " + settled + " A2A tasks left running by a previous process as failed");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                context.Logging.Warn("[Agent] A2A task recovery failed: " + SecretScrubber.Shared.Scrub(e.Message));
            }
        }

        /// <summary>
        /// Cancel running turns and wait briefly for background A2A tasks.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StopAsync(CancellationToken token)
        {
            _Host?.Turns.CancelAll();
            if (_Tasks != null) await _Tasks.DrainAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        #endregion
    }
}
