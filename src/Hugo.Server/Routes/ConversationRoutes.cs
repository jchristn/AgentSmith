namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Requests;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Conversation and chat routes (P6.15): CRUD with the visibility rule (P6.24), messages in SSE or JSON mode, cancel,
    /// and export. Flat paths; the tenant comes from the request context (D-040).
    /// </summary>
    public class ConversationRoutes
    {
        #region Private-Members

        private static readonly string _Base = "/v1.0/api/conversations";
        private readonly ServerContext _Server;
        private readonly ConversationService _Conversations;
        private readonly AgentTurnHost _Host;
        private readonly ProfileResolver _Resolver;
        private RouteRegistrar? _Routes = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Server context.</param>
        /// <param name="conversations">Conversation service.</param>
        /// <param name="host">Agent turn host.</param>
        /// <param name="resolver">Profile resolver.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ConversationRoutes(ServerContext server, ConversationService conversations, AgentTurnHost host, ProfileResolver resolver)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
            _Host = host ?? throw new ArgumentNullException(nameof(host));
            _Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        /// <exception cref="ArgumentNullException">Thrown when routes is null.</exception>
        public void Register(RouteRegistrar routes)
        {
            _Routes = routes ?? throw new ArgumentNullException(nameof(routes));
            routes.Scoped(HttpMethod.GET, _Base, ResourceTypeEnum.Conversation, OperationTypeEnum.Read, ListAsync, "List the caller's conversations (tenant administrators see all; filters: agentProfileId, source, search)", "Conversations", null, typeof(EnumerationResult<Conversation>), "none");
            routes.Scoped(HttpMethod.POST, _Base, ResourceTypeEnum.Conversation, OperationTypeEnum.Create, CreateAsync, "Start a conversation with an agent profile (default profile per D-018 when none is given)", "Conversations", typeof(ConversationRequest), typeof(Conversation), "none");
            routes.Scoped(HttpMethod.GET, _Base + "/{id}", ResourceTypeEnum.Conversation, OperationTypeEnum.Read, ReadAsync, "Read a conversation", "Conversations", null, typeof(Conversation));
            routes.Scoped(HttpMethod.PUT, _Base + "/{id}", ResourceTypeEnum.Conversation, OperationTypeEnum.Update, UpdateAsync, "Rename a conversation", "Conversations", typeof(ConversationRequest), typeof(Conversation));
            routes.Authenticated(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, "Delete a conversation and its messages (its owner, or Delete on Conversation)", "Conversations", null, null, ResourceTypeEnum.Conversation, OperationTypeEnum.Delete);
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/messages", ResourceTypeEnum.Conversation, OperationTypeEnum.Read, MessagesAsync, "List a conversation's messages in order (compacted ones included, with their tool calls)", "Conversations", null, typeof(EnumerationResult<ConversationMessage>));
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/messages", ResourceTypeEnum.Conversation, OperationTypeEnum.Update, SendAsync, "Send a message and run a turn: Server-Sent Events (D-022 named events) with Accept: text/event-stream, otherwise the turn result as JSON. Also needs Execute on the agent profile", "Conversations", typeof(ChatMessageRequest), typeof(AgentTurnResult));
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/cancel", ResourceTypeEnum.Conversation, OperationTypeEnum.Update, CancelAsync, "Cancel the turn running in a conversation", "Conversations", null, typeof(ConversationCancelResponse));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/export", ResourceTypeEnum.Conversation, OperationTypeEnum.Read, ExportAsync, "Export a conversation (format=md or json)", "Conversations");
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase http)
        {
            ConversationFilter filter = new ConversationFilter();
            filter.AgentProfileId = RouteHelpers.Query(http, "agentProfileId");
            filter.Source = RouteHelpers.QueryEnum<ConversationSourceEnum>(http, "source");
            EnumerationResult<Conversation> page = await _Conversations.ListAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Enumeration(http), filter, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, page).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            string tenantId = RouteHelpers.Tenant(http);
            ConversationRequest request = RouteHelpers.Body<ConversationRequest>(http) ?? new ConversationRequest();
            ProfileResolution resolution = await _Resolver.ResolveAsync(ctx, tenantId, request.AgentProfileId ?? http.Request.Headers["x-hugo-profile"], http.Token).ConfigureAwait(false);
            Conversation created = await _Conversations.CreateAsync(ctx, tenantId, resolution.Profile, ConversationSourceEnum.Dashboard, request.Title, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase http)
        {
            Conversation conversation = await _Conversations.ReadAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, conversation).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase http)
        {
            Conversation updated = await _Conversations.UpdateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, RouteHelpers.RequiredBody<ConversationRequest>(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, updated).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            string tenantId = RouteHelpers.Tenant(http);
            string id = RouteHelpers.Param(http, "id")!;
            Conversation conversation = await _Conversations.ReadAsync(ctx, tenantId, id, http.Token).ConfigureAwait(false);
            bool owner = !ConversationService.SeesAll(ctx) && ConversationService.CanSee(ctx, conversation);
            if (!owner && !await _Routes!.RequireAsync(http, tenantId, ResourceTypeEnum.Conversation, OperationTypeEnum.Delete, conversation.Id).ConfigureAwait(false)) return;
            await _Conversations.DeleteAsync(ctx, tenantId, id, http.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(http).ConfigureAwait(false);
        }

        private async Task MessagesAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            string tenantId = RouteHelpers.Tenant(http);
            Conversation conversation = await _Conversations.ReadAsync(ctx, tenantId, RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            EnumerationQuery query = RouteHelpers.Enumeration(http);
            if (RouteHelpers.QueryInt(http, "maxResults") == null) query.MaxResults = 1000;
            EnumerationResult<ConversationMessage> page = await _Server.Database.Messages.EnumerateAsync(tenantId, conversation.Id, query, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, page).ConfigureAwait(false);
        }

        private async Task SendAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            string tenantId = RouteHelpers.Tenant(http);
            Conversation conversation = await _Conversations.ReadAsync(ctx, tenantId, RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            ChatMessageRequest request = RouteHelpers.Body<ChatMessageRequest>(http) ?? new ChatMessageRequest();
            AgentProfile? profile = await _Server.Database.AgentProfiles.ReadAsync(tenantId, conversation.AgentProfileId, http.Token).ConfigureAwait(false);
            if (profile == null || !profile.Active || !ResourceVisibility.CanSee(profile, ctx)) throw new NotFoundException("The conversation's agent profile no longer exists or is not available to you.");
            if (!await _Routes!.RequireAsync(http, tenantId, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute, profile.Id).ConfigureAwait(false)) return;
            Credential? credential = ctx.Credential;
            if (credential != null && credential.LockToProfile && !String.Equals(credential.AgentProfileId, profile.Id, StringComparison.Ordinal))
                throw new ForbiddenException("ProfileLocked", "This credential is locked to another agent profile.");

            AgentTurnOptions options = new AgentTurnOptions();
            options.Principal = ctx;
            options.TenantId = tenantId;
            options.Profile = profile;
            options.Conversation = conversation;
            options.UserMessage = request.Message ?? String.Empty;
            options.Surface = InvocationSurfaceEnum.Chat;
            options.WaitMode = ApprovalWaitModeEnum.Wait;

            bool streaming = WantsEventStream(http);
            AgentTurnPlan plan;
            try
            {
                plan = await _Host.PrepareAsync(options, http.Token).ConfigureAwait(false);
            }
            catch (AgentTurnConflictException e)
            {
                await RouteHelpers.ErrorAsync(http, 409, "TurnInProgress", e.Message).ConfigureAwait(false);
                return;
            }

            using (plan)
            {
                if (!streaming)
                {
                    AgentTurnResult result = await _Host.RunAsync(plan, http.Token).ConfigureAwait(false);
                    await RouteHelpers.JsonAsync(http, 200, result).ConfigureAwait(false);
                    return;
                }

                ctx.SuppressHistoryBodies = true;
                using (SseWriter writer = new SseWriter(http, "chat"))
                {
                    writer.Start();
                    options.Sinks.Add(new SseAgentEventSink(writer));
                    AgentTurnResult result = await _Host.RunAsync(plan, http.Token).ConfigureAwait(false);
                    Conversation? latest = await _Server.Database.Conversations.ReadAsync(tenantId, conversation.Id, System.Threading.CancellationToken.None).ConfigureAwait(false);
                    ConversationStreamClosed closed = new ConversationStreamClosed();
                    closed.ConversationId = conversation.Id;
                    closed.TurnId = result.TurnId;
                    closed.FinishReason = AgentTurnHost.ReasonLabel(result.FinishReason);
                    closed.Title = latest?.Title;
                    await writer.CloseAsync("stream.closed", Serializer.Serialize(closed), System.Threading.CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private async Task CancelAsync(HttpContextBase http)
        {
            Conversation conversation = await _Conversations.ReadAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            ConversationCancelResponse response = new ConversationCancelResponse();
            response.ConversationId = conversation.Id;
            response.Cancelled = _Host.Turns.Cancel(conversation.Id);
            await RouteHelpers.JsonAsync(http, 200, response).ConfigureAwait(false);
        }

        private async Task ExportAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            string tenantId = RouteHelpers.Tenant(http);
            Conversation conversation = await _Conversations.ReadAsync(ctx, tenantId, RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            string format = (RouteHelpers.Query(http, "format") ?? "md").Trim().ToLowerInvariant();
            if (format != "md" && format != "markdown" && format != "json")
            {
                await RouteHelpers.ErrorAsync(http, 400, "BadRequest", "format must be md or json.").ConfigureAwait(false);
                return;
            }
            List<ConversationMessage> messages = await _Server.Database.Messages.ListAsync(tenantId, conversation.Id, false, http.Token).ConfigureAwait(false);
            string body;
            string extension;
            if (format == "json")
            {
                body = ConversationService.ExportJson(conversation, messages);
                http.Response.ContentType = "application/json";
                extension = "json";
            }
            else
            {
                AgentProfile? profile = await _Server.Database.AgentProfiles.ReadAsync(tenantId, conversation.AgentProfileId, http.Token).ConfigureAwait(false);
                body = ConversationService.ExportMarkdown(conversation, profile, messages);
                http.Response.ContentType = "text/markdown; charset=utf-8";
                extension = "md";
            }
            http.Response.StatusCode = 200;
            http.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + conversation.Id + "." + extension + "\"";
            await http.Response.Send(body, http.Token).ConfigureAwait(false);
        }

        private static bool WantsEventStream(HttpContextBase http)
        {
            string? accept = http.Request.Headers["Accept"];
            return !String.IsNullOrEmpty(accept) && accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
