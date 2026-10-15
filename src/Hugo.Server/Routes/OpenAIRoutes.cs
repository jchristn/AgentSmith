namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Server.Protocols.OpenAI;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// OpenAI-compatible surface (P7.18, plan 14.5): chat completions (streaming and not) that run Hugo's own loop, with
    /// the profile name as the model, and the list of profiles the caller can use. Client tools are rejected; client
    /// system messages are appended after the compiled instructions as caller context. The <c>user</c> field or the
    /// <c>x-hugo-conversation</c> header keeps a server-side conversation; without them each request starts a
    /// conversation from the supplied messages. A tool call that needs approval ends the turn with an assistant message
    /// explaining it; the next message reports the outcome once a human decides.
    /// </summary>
    public class OpenAIRoutes
    {
        #region Private-Members

        private static readonly string _Base = "/v1.0/openai/v1";
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        private readonly ServerContext _Server;
        private readonly AgentTurnHost _Host;
        private readonly ConversationService _Conversations;
        private readonly ProfileResolver _Resolver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Server context.</param>
        /// <param name="host">Agent turn host.</param>
        /// <param name="conversations">Conversation service.</param>
        /// <param name="resolver">Profile resolver.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public OpenAIRoutes(ServerContext server, AgentTurnHost host, ConversationService conversations, ProfileResolver resolver)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Host = host ?? throw new ArgumentNullException(nameof(host));
            _Conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
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
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            routes.Authenticated(HttpMethod.POST, _Base + "/chat/completions", CompletionsAsync, "OpenAI-compatible chat completions running the profile's own agent loop (model = profile name; Execute on that profile)", "OpenAI", typeof(OpenAIChatRequest), typeof(OpenAIChatResponse), ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute);
            routes.Authenticated(HttpMethod.GET, _Base + "/models", ModelsAsync, "Agent profiles the caller can use, as OpenAI models", "OpenAI", null, typeof(OpenAIModelList), ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read);
        }

        #endregion

        #region Private-Methods

        private async Task ModelsAsync(HttpContextBase http)
        {
            if (!_Server.Settings.OpenAI.Enable)
            {
                await ErrorAsync(http, 404, "not_found_error", "The OpenAI-compatible endpoint is disabled.", null, "disabled").ConfigureAwait(false);
                return;
            }
            RequestContext ctx = RouteHelpers.Context(http);
            string tenantId = RouteHelpers.Tenant(http);
            OpenAIModelList list = new OpenAIModelList();
            foreach (AgentProfile profile in (await _Server.Database.AgentProfiles.ListAllAsync(tenantId, http.Token).ConfigureAwait(false)).Where(p => p.Active && p.ExposeOpenAI && ResourceVisibility.CanSee(p, ctx)).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                Credential? credential = ctx.Credential;
                if (credential != null && credential.LockToProfile && !String.Equals(credential.AgentProfileId, profile.Id, StringComparison.Ordinal)) continue;
                AuthorizationDecision decision = await _Server.Authorization.EvaluateAsync(ctx, tenantId, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute, profile.Id, http.Token).ConfigureAwait(false);
                if (!decision.Permitted) continue;
                OpenAIModel model = new OpenAIModel();
                model.Id = profile.Name;
                model.Created = new DateTimeOffset(DateTime.SpecifyKind(profile.CreatedUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
                list.Data.Add(model);
            }
            await JsonAsync(http, 200, list).ConfigureAwait(false);
        }

        private async Task CompletionsAsync(HttpContextBase http)
        {
            if (!_Server.Settings.OpenAI.Enable)
            {
                await ErrorAsync(http, 404, "not_found_error", "The OpenAI-compatible endpoint is disabled.", null, "disabled").ConfigureAwait(false);
                return;
            }
            RequestContext ctx = RouteHelpers.Context(http);
            string tenantId = RouteHelpers.Tenant(http);
            OpenAIChatRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<OpenAIChatRequest>(http.Request.DataAsString ?? String.Empty, _Json);
            }
            catch (JsonException e)
            {
                await ErrorAsync(http, 400, "invalid_request_error", "The request body is not valid JSON: " + e.Message, null, "invalid_json").ConfigureAwait(false);
                return;
            }
            if (request == null)
            {
                await ErrorAsync(http, 400, "invalid_request_error", "A JSON request body is required.", null, "invalid_json").ConfigureAwait(false);
                return;
            }
            if ((request.Tools != null && request.Tools.Count > 0) || (request.Functions != null && request.Functions.Count > 0))
            {
                await ErrorAsync(http, 400, "invalid_request_error", "Client-supplied tools are not supported: Hugo runs its own tools for the agent profile.", "tools", "tools_not_supported").ConfigureAwait(false);
                return;
            }
            List<OpenAIChatMessage> messages = request.Messages ?? new List<OpenAIChatMessage>();
            if (messages.Any(m => String.Equals(m.Role, "tool", StringComparison.Ordinal) || (m.ToolCalls != null && m.ToolCalls.Count > 0)))
            {
                await ErrorAsync(http, 400, "invalid_request_error", "Tool messages and tool calls are not supported: Hugo runs its own tools for the agent profile.", "messages", "tools_not_supported").ConfigureAwait(false);
                return;
            }
            OpenAIChatMessage? last = messages.LastOrDefault(m => !IsSystem(m));
            if (last == null || !String.Equals(last.Role, "user", StringComparison.Ordinal) || String.IsNullOrWhiteSpace(last.Content))
            {
                await ErrorAsync(http, 400, "invalid_request_error", "The last message must be a user message with text content.", "messages", "invalid_messages").ConfigureAwait(false);
                return;
            }
            if (String.IsNullOrWhiteSpace(request.Model))
            {
                await ErrorAsync(http, 400, "invalid_request_error", "model is required: use an agent profile name from GET /v1.0/openai/v1/models.", "model", "model_required").ConfigureAwait(false);
                return;
            }

            AgentProfile profile;
            try
            {
                profile = (await _Resolver.ResolveAsync(ctx, tenantId, request.Model, http.Token).ConfigureAwait(false)).Profile;
            }
            catch (NotFoundException)
            {
                await ErrorAsync(http, 404, "not_found_error", "The model '" + request.Model + "' does not exist or you do not have access to it.", "model", "model_not_found").ConfigureAwait(false);
                return;
            }
            catch (ForbiddenException e)
            {
                await ErrorAsync(http, 403, "permission_error", e.Message, "model", e.Code).ConfigureAwait(false);
                return;
            }
            if (!profile.ExposeOpenAI)
            {
                await ErrorAsync(http, 404, "not_found_error", "The model '" + request.Model + "' does not exist or you do not have access to it.", "model", "model_not_found").ConfigureAwait(false);
                return;
            }
            AuthorizationDecision decision = await _Server.Authorization.AuthorizeAsync(ctx, tenantId, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute, profile.Id, http.Token).ConfigureAwait(false);
            if (!decision.Permitted)
            {
                await ErrorAsync(http, decision.Result == Hugo.Core.Enums.AuthorizationResultEnum.NotFound ? 404 : 403, decision.Result == Hugo.Core.Enums.AuthorizationResultEnum.NotFound ? "not_found_error" : "permission_error", decision.Result == Hugo.Core.Enums.AuthorizationResultEnum.NotFound ? "The model '" + request.Model + "' does not exist or you do not have access to it." : "You may not use agent profile " + profile.Name + " (Execute on AgentProfile is required).", "model", decision.Result == Hugo.Core.Enums.AuthorizationResultEnum.NotFound ? "model_not_found" : "AuthorizationDenied").ConfigureAwait(false);
                return;
            }

            Conversation conversation;
            try
            {
                conversation = await ConversationAsync(http, ctx, tenantId, profile, request, messages).ConfigureAwait(false);
            }
            catch (NotFoundException e)
            {
                await ErrorAsync(http, 404, "not_found_error", e.Message, "x-hugo-conversation", "conversation_not_found").ConfigureAwait(false);
                return;
            }
            catch (ValidationException e)
            {
                await ErrorAsync(http, 400, "invalid_request_error", e.Message, "x-hugo-conversation", e.Code).ConfigureAwait(false);
                return;
            }

            AgentTurnOptions options = new AgentTurnOptions();
            options.Principal = ctx;
            options.TenantId = tenantId;
            options.Profile = profile;
            options.Conversation = conversation;
            options.UserMessage = last.Content!;
            options.Surface = InvocationSurfaceEnum.OpenAI;
            options.WaitMode = ApprovalWaitModeEnum.NoWait;
            List<string> system = messages.Where(IsSystem).Select(m => m.Content ?? String.Empty).Where(c => c.Trim().Length > 0).ToList();
            if (system.Count > 0) options.CallerContext = String.Join("\n\n", system);

            AgentTurnPlan plan;
            try
            {
                plan = await _Host.PrepareAsync(options, http.Token).ConfigureAwait(false);
            }
            catch (AgentTurnConflictException e)
            {
                await ErrorAsync(http, 409, "conflict_error", e.Message, null, "turn_in_progress").ConfigureAwait(false);
                return;
            }
            catch (ValidationException e)
            {
                await ErrorAsync(http, 400, "invalid_request_error", e.Message, null, e.Code).ConfigureAwait(false);
                return;
            }

            http.Response.Headers["x-hugo-conversation"] = conversation.Id;
            long created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string id = "chatcmpl-" + Guid.NewGuid().ToString("N");
            using (plan)
            {
                if (request.Stream != true)
                {
                    AgentTurnResult result = await _Host.RunAsync(plan, http.Token).ConfigureAwait(false);
                    if (result.FinishReason == AgentFinishReasonEnum.Error)
                    {
                        await ErrorAsync(http, 502, "server_error", "The agent turn failed: " + (SecretScrubber.Shared.Scrub(result.ErrorMessage) ?? "unknown error"), null, result.ErrorCode ?? "agent_error").ConfigureAwait(false);
                        return;
                    }
                    OpenAIChatResponse response = new OpenAIChatResponse();
                    response.Id = id;
                    response.Created = created;
                    response.Model = profile.Name;
                    response.Choices.Add(new OpenAIChoice { Index = 0, Message = new OpenAIChatMessage { Role = "assistant", Content = result.FinalText }, FinishReason = FinishReason(result) });
                    response.Usage = Usage(result);
                    response.Hugo = Info(conversation, result);
                    await JsonAsync(http, 200, response).ConfigureAwait(false);
                    return;
                }

                ctx.SuppressHistoryBodies = true;
                using (SseWriter writer = new SseWriter(http, "openai"))
                {
                    writer.RetryMs = 0;
                    writer.Start(false);
                    OpenAIStreamSink sink = new OpenAIStreamSink(writer, id, profile.Name, created, _Json);
                    options.Sinks.Add(sink);
                    await sink.EnsureRoleAsync(http.Token).ConfigureAwait(false);
                    AgentTurnResult result = await _Host.RunAsync(plan, http.Token).ConfigureAwait(false);
                    if (result.FinishReason == AgentFinishReasonEnum.Error)
                    {
                        OpenAIErrorResponse failure = new OpenAIErrorResponse();
                        failure.Error.Type = "server_error";
                        failure.Error.Code = result.ErrorCode ?? "agent_error";
                        failure.Error.Message = "The agent turn failed: " + (SecretScrubber.Shared.Scrub(result.ErrorMessage) ?? "unknown error");
                        await writer.SendAsync(null, JsonSerializer.Serialize(failure, _Json), null, CancellationToken.None).ConfigureAwait(false);
                    }
                    OpenAIChatResponse final = sink.Chunk(new OpenAIChatMessage(), FinishReason(result));
                    final.Hugo = Info(conversation, result);
                    await writer.SendAsync(null, JsonSerializer.Serialize(final, _Json), null, CancellationToken.None).ConfigureAwait(false);
                    if (request.StreamOptions != null && request.StreamOptions.IncludeUsage)
                    {
                        OpenAIChatResponse usage = sink.Chunk(null, null);
                        usage.Choices = new List<OpenAIChoice>();
                        usage.Usage = Usage(result);
                        await writer.SendAsync(null, JsonSerializer.Serialize(usage, _Json), null, CancellationToken.None).ConfigureAwait(false);
                    }
                    await writer.CloseAsync(null, "[DONE]", CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private async Task<Conversation> ConversationAsync(HttpContextBase http, RequestContext ctx, string tenantId, AgentProfile profile, OpenAIChatRequest request, List<OpenAIChatMessage> messages)
        {
            string? header = http.Request.Headers["x-hugo-conversation"];
            Conversation? conversation = null;
            if (!String.IsNullOrWhiteSpace(header))
            {
                conversation = await _Conversations.ReadAsync(ctx, tenantId, header.Trim(), http.Token).ConfigureAwait(false);
                if (!String.Equals(conversation.AgentProfileId, profile.Id, StringComparison.Ordinal)) throw new ValidationException("Conversation " + conversation.Id + " belongs to another agent profile.") { Code = "conversation_profile_mismatch" };
            }
            else if (!String.IsNullOrWhiteSpace(request.User))
            {
                string key = request.User.Trim();
                if (key.Length > 256) throw new ValidationException("user is longer than 256 characters.") { Code = "invalid_user" };
                string? credentialId = ctx.PrincipalType == PrincipalTypeEnum.Credential ? ctx.CredentialId : null;
                conversation = await _Server.Database.Conversations.ReadByExternalKeyAsync(tenantId, profile.Id, ctx.UserId, credentialId, key, http.Token).ConfigureAwait(false);
                if (conversation == null)
                {
                    conversation = ConversationService.New(ctx, tenantId, profile, ConversationSourceEnum.OpenAI);
                    conversation.ExternalKey = key;
                    conversation = await _Server.Database.Conversations.CreateAsync(conversation, http.Token).ConfigureAwait(false);
                }
            }
            else
            {
                conversation = ConversationService.New(ctx, tenantId, profile, ConversationSourceEnum.OpenAI);
                conversation = await _Server.Database.Conversations.CreateAsync(conversation, http.Token).ConfigureAwait(false);
            }

            if (conversation.MessageCount == 0)
            {
                // A new conversation starts from the history the client supplied (everything before the last user message).
                List<OpenAIChatMessage> prior = messages.Where(m => !IsSystem(m)).ToList();
                prior.RemoveAt(prior.Count - 1);
                List<ConversationMessage> seed = new List<ConversationMessage>();
                foreach (OpenAIChatMessage m in prior.Where(m => !String.IsNullOrWhiteSpace(m.Content)))
                {
                    ConversationMessage row = new ConversationMessage();
                    row.Role = String.Equals(m.Role, "assistant", StringComparison.Ordinal) ? AgentMessageRoleEnum.Assistant : AgentMessageRoleEnum.User;
                    row.Content = m.Content!;
                    seed.Add(row);
                }
                if (seed.Count > 0) await _Server.Database.Messages.AppendAsync(tenantId, conversation.Id, seed, http.Token).ConfigureAwait(false);
            }
            return conversation;
        }

        private static bool IsSystem(OpenAIChatMessage message)
        {
            return String.Equals(message.Role, "system", StringComparison.Ordinal) || String.Equals(message.Role, "developer", StringComparison.Ordinal);
        }

        private static string FinishReason(AgentTurnResult result)
        {
            return result.FinishReason == AgentFinishReasonEnum.MaxIterations || result.FinishReason == AgentFinishReasonEnum.ToolCallLimit ? "length" : "stop";
        }

        private static OpenAIUsage Usage(AgentTurnResult result)
        {
            OpenAIUsage usage = new OpenAIUsage();
            usage.PromptTokens = result.TokensIn;
            usage.CompletionTokens = result.TokensOut;
            usage.TotalTokens = result.TokensIn + result.TokensOut;
            return usage;
        }

        private static OpenAIHugoInfo Info(Conversation conversation, AgentTurnResult result)
        {
            OpenAIHugoInfo info = new OpenAIHugoInfo();
            info.ConversationId = conversation.Id;
            info.TurnId = result.TurnId;
            info.FinishReason = AgentTurnHost.ReasonLabel(result.FinishReason);
            info.PendingApprovalId = result.PendingApprovalId;
            return info;
        }

        private static async Task JsonAsync(HttpContextBase http, int status, object body)
        {
            http.Response.StatusCode = status;
            http.Response.ContentType = "application/json";
            await http.Response.Send(JsonSerializer.Serialize(body, body.GetType(), _Json), http.Token).ConfigureAwait(false);
        }

        private static async Task ErrorAsync(HttpContextBase http, int status, string type, string message, string? param, string? code)
        {
            OpenAIErrorResponse error = new OpenAIErrorResponse();
            error.Error.Type = type;
            error.Error.Message = message;
            error.Error.Param = param;
            error.Error.Code = code;
            await JsonAsync(http, status, error).ConfigureAwait(false);
        }

        #endregion
    }
}
