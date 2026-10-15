namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Protocols;
    using Hugo.Server.Services;
    using Voltaic.A2A;
    using Voltaic.Core;
    using WatsonWebserver.Core;

    /// <summary>
    /// A2A 1.0 surface (P7.13 to P7.17, D-006), adapted from Garrison A2ARoutes: the public agent card, the tenant card
    /// list, and the JSON-RPC endpoint with the 1.0 and legacy method names. Tasks are persisted with principal ownership:
    /// another principal gets -32001 (task not found) unless it is a tenant administrator, and refusals use -32007. Each
    /// JSON-RPC method has an entry in the operation scope map's protocol methods and fails closed without one.
    /// </summary>
    public class A2ARoutes
    {
        #region Public-Members

        /// <summary>
        /// Protocol name used for the A2A methods in the operation scope map.
        /// </summary>
        public static readonly string Protocol = "A2A";

        /// <summary>
        /// Seconds a blocking SendMessage waits for the task to finish or need input before returning it as it stands.
        /// Default 300, minimum 1, maximum 3600.
        /// </summary>
        public int SendMessageWaitSeconds
        {
            get
            {
                return _SendMessageWaitSeconds;
            }
            set
            {
                _SendMessageWaitSeconds = Math.Clamp(value, 1, 3600);
            }
        }

        #endregion

        #region Private-Members

        private readonly ServerContext _Server;
        private readonly A2ATaskService _Tasks;
        private readonly AgentCardService _Cards;
        private int _SendMessageWaitSeconds = 300;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Server context.</param>
        /// <param name="tasks">Task service.</param>
        /// <param name="cards">Card service.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public A2ARoutes(ServerContext server, A2ATaskService tasks, AgentCardService cards)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            _Cards = cards ?? throw new ArgumentNullException(nameof(cards));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the routes and map every JSON-RPC method in the operation scope map.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        /// <exception cref="ArgumentNullException">Thrown when routes is null.</exception>
        public void Register(RouteRegistrar routes)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            routes.Anonymous(HttpMethod.GET, "/v1.0/a2a/{tenantId}/{profileName}/.well-known/agent-card.json", CardAsync, "A2A agent card of a profile (exposed profiles only)", "A2A");
            routes.Scoped(HttpMethod.GET, "/v1.0/a2a/{tenantId}", ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, CatalogAsync, "A2A agent cards of every exposed profile in a tenant", "A2A", null, null, "none");
            routes.Authenticated(HttpMethod.POST, "/v1.0/a2a/{tenantId}/{profileName}", JsonRpcAsync, "A2A JSON-RPC: SendMessage, SendStreamingMessage (SSE), GetTask, ListTasks, CancelTask, SubscribeToTask (SSE), GetExtendedAgentCard, and the legacy message/send, message/stream, tasks/get, tasks/list, tasks/cancel, tasks/resubscribe", "A2A", null, null, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute);

            OperationScopeMap scopes = routes.Scopes;
            Action<string, ResourceTypeEnum, OperationTypeEnum, string> map = (method, resourceType, operation, summary) => scopes.AddProtocolOperation(new OperationScope { Method = Protocol, Path = method, ResourceType = resourceType, Operation = operation, Authorization = RouteAuthorizationEnum.Scoped, Tag = Protocol, Summary = summary });
            map(A2AProtocol.SendMessage, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute, "Send a message; the task runs Hugo's own loop");
            map(A2AProtocol.SendStreamingMessage, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute, "Send a message and stream the task");
            map(A2AProtocol.GetTask, ResourceTypeEnum.Conversation, OperationTypeEnum.Read, "Read a task (owner or tenant administrator)");
            map(A2AProtocol.ListTasks, ResourceTypeEnum.Conversation, OperationTypeEnum.Read, "List the caller's tasks (tenant administrators see all)");
            map(A2AProtocol.CancelTask, ResourceTypeEnum.Conversation, OperationTypeEnum.Update, "Cancel a task (owner or tenant administrator)");
            map(A2AProtocol.SubscribeToTask, ResourceTypeEnum.Conversation, OperationTypeEnum.Read, "Stream a task's updates");
            map(A2AProtocol.GetExtendedAgentCard, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, "The agent card, for an authenticated caller");
        }

        /// <summary>
        /// Canonical (1.0) method name of a request's method, or null for an unknown method.
        /// </summary>
        /// <param name="method">Method as received.</param>
        /// <returns>Canonical name, or null.</returns>
        public static string? Canonical(string? method)
        {
            switch (method)
            {
                case A2AProtocol.SendMessage:
                case "message/send":
                    return A2AProtocol.SendMessage;
                case A2AProtocol.SendStreamingMessage:
                case "message/stream":
                    return A2AProtocol.SendStreamingMessage;
                case A2AProtocol.GetTask:
                case "tasks/get":
                    return A2AProtocol.GetTask;
                case A2AProtocol.ListTasks:
                case "tasks/list":
                    return A2AProtocol.ListTasks;
                case A2AProtocol.CancelTask:
                case "tasks/cancel":
                    return A2AProtocol.CancelTask;
                case A2AProtocol.SubscribeToTask:
                case "tasks/resubscribe":
                    return A2AProtocol.SubscribeToTask;
                case A2AProtocol.GetExtendedAgentCard:
                case "agent/getAuthenticatedExtendedCard":
                    return A2AProtocol.GetExtendedAgentCard;
                default:
                    return null;
            }
        }

        #endregion

        #region Private-Methods

        private async Task CardAsync(HttpContextBase http)
        {
            string tenantId = RouteHelpers.Param(http, "tenantId") ?? String.Empty;
            AgentProfile? profile = await ExposedAsync(tenantId, RouteHelpers.Param(http, "profileName"), http.Token).ConfigureAwait(false);
            if (profile == null || profile.Visibility != VisibilityEnum.Tenant)
            {
                await RouteHelpers.NotFoundAsync(http, "Agent").ConfigureAwait(false);
                return;
            }
            AgentCard card = await _Cards.BuildAsync(tenantId, profile, ExternalUrls.RestBaseUrl(_Server, http), http.Token).ConfigureAwait(false);
            await SendJsonAsync(http, 200, card).ConfigureAwait(false);
        }

        private async Task CatalogAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            string tenantId = RouteHelpers.Param(http, "tenantId") ?? String.Empty;
            List<AgentCard> cards = new List<AgentCard>();
            if (_Server.Settings.A2A.Enable)
            {
                foreach (AgentProfile profile in (await _Server.Database.AgentProfiles.ListAllAsync(tenantId, http.Token).ConfigureAwait(false)).Where(p => p.Active && p.ExposeA2A && ResourceVisibility.CanSee(p, ctx)).OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    cards.Add(await _Cards.BuildAsync(tenantId, profile, ExternalUrls.RestBaseUrl(_Server, http), http.Token).ConfigureAwait(false));
                }
            }
            await SendJsonAsync(http, 200, cards).ConfigureAwait(false);
        }

        private async Task JsonRpcAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            string tenantId = RouteHelpers.Param(http, "tenantId") ?? String.Empty;
            string profileName = RouteHelpers.Param(http, "profileName") ?? String.Empty;
            ctx.SuppressHistoryBodies = true;

            A2AJsonRpcRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<A2AJsonRpcRequest>(http.Request.DataAsString ?? String.Empty, A2AJson.DefaultOptions);
            }
            catch (JsonException)
            {
                request = null;
            }
            object? id = request?.Id;
            string? method = Canonical(request?.Method);
            string label = method ?? "unknown";
            string outcome = "error";
            Activity? span = AgentTelemetry.StartActivity("a2a " + label, ActivityKind.Server);
            AgentTelemetry.SetTag(span, "rpc.system", "jsonrpc");
            AgentTelemetry.SetTag(span, "rpc.method", label);
            AgentTelemetry.SetTag(span, "hugo.tenant", tenantId);
            try
            {
                if ((!String.Equals(ctx.TenantId, tenantId, StringComparison.Ordinal) && !ctx.IsAdmin) || !_Server.Settings.A2A.Enable)
                {
                    await SendErrorAsync(http, id, Error(-32001, "Agent " + profileName + " not found.", "NotFound"), 404).ConfigureAwait(false);
                    return;
                }
                if (request == null)
                {
                    await SendErrorAsync(http, null, JsonRpcError.ParseError(), 200).ConfigureAwait(false);
                    return;
                }
                AgentProfile? profile = await ExposedAsync(tenantId, profileName, http.Token).ConfigureAwait(false);
                if (profile == null || !ResourceVisibility.CanSee(profile, ctx))
                {
                    await SendErrorAsync(http, id, Error(-32001, "Agent " + profileName + " not found.", "NotFound"), 404).ConfigureAwait(false);
                    return;
                }
                if (method == null)
                {
                    await SendErrorAsync(http, id, JsonRpcError.MethodNotFound(), 200).ConfigureAwait(false);
                    return;
                }
                OperationScope? scope = _Server.Scopes.FindProtocolOperation(Protocol, method);
                if (scope == null || !scope.ResourceType.HasValue || !scope.Operation.HasValue)
                {
                    if (!ctx.IsAdmin) throw new A2AAccessException(-32007, "Method " + method + " has no authorization mapping and is restricted to system administrators.", "RouteNotMapped");
                }

                try
                {
                    switch (method)
                    {
                        case A2AProtocol.SendMessage:
                        case A2AProtocol.SendStreamingMessage:
                            await RequireAsync(ctx, tenantId, scope, profile.Id, http.Token).ConfigureAwait(false);
                            await SendMessageAsync(http, ctx, tenantId, profile, id, Parse<SendMessageRequest>(request.Params), method == A2AProtocol.SendStreamingMessage).ConfigureAwait(false);
                            break;
                        case A2AProtocol.GetTask:
                            A2ATask read = await OwnedTaskAsync(ctx, tenantId, profile, Parse<GetTaskRequest>(request.Params).Id, http.Token).ConfigureAwait(false);
                            await RequireAsync(ctx, tenantId, scope, read.ConversationId, http.Token).ConfigureAwait(false);
                            await SendResultAsync(http, id, await WireAsync(read, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
                            break;
                        case A2AProtocol.ListTasks:
                            await RequireAsync(ctx, tenantId, scope, null, http.Token).ConfigureAwait(false);
                            await ListTasksAsync(http, ctx, tenantId, profile, id, Parse<ListTasksRequest>(request.Params)).ConfigureAwait(false);
                            break;
                        case A2AProtocol.CancelTask:
                            A2ATask toCancel = await OwnedTaskAsync(ctx, tenantId, profile, Parse<CancelTaskRequest>(request.Params).Id, http.Token).ConfigureAwait(false);
                            await RequireAsync(ctx, tenantId, scope, toCancel.ConversationId, http.Token).ConfigureAwait(false);
                            A2ATask cancelled = await _Tasks.CancelAsync(ctx, toCancel, http.Token).ConfigureAwait(false);
                            await SendResultAsync(http, id, await WireAsync(cancelled, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
                            break;
                        case A2AProtocol.SubscribeToTask:
                            A2ATask watched = await OwnedTaskAsync(ctx, tenantId, profile, Parse<SubscribeToTaskRequest>(request.Params).Id, http.Token).ConfigureAwait(false);
                            await RequireAsync(ctx, tenantId, scope, watched.ConversationId, http.Token).ConfigureAwait(false);
                            await StreamAsync(http, id, watched).ConfigureAwait(false);
                            break;
                        default:
                            await RequireAsync(ctx, tenantId, scope, profile.Id, http.Token).ConfigureAwait(false);
                            await SendResultAsync(http, id, await _Cards.BuildAsync(tenantId, profile, ExternalUrls.RestBaseUrl(_Server, http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
                            break;
                    }
                    outcome = "ok";
                }
                catch (A2AAccessException e)
                {
                    await SendErrorAsync(http, id, Error(e.Code, e.Message, e.DataCode), 200).ConfigureAwait(false);
                }
                catch (NotFoundException e)
                {
                    await SendErrorAsync(http, id, Error((int)A2AErrorCode.TaskNotFound, e.Message, "NotFound"), 200).ConfigureAwait(false);
                }
                catch (ValidationException e)
                {
                    JsonRpcError error = Error(-32602, e.Describe(), e.Code);
                    if (e.Fields.Count > 0) ((A2AErrorData)error.Data!).Errors = e.Fields;
                    await SendErrorAsync(http, id, error, 200).ConfigureAwait(false);
                }
                catch (ForbiddenException e)
                {
                    await SendErrorAsync(http, id, Error(-32007, e.Message, e.Code), 200).ConfigureAwait(false);
                }
                catch (ConflictException e)
                {
                    await SendErrorAsync(http, id, Error(-32007, e.Message, e.Code), 200).ConfigureAwait(false);
                }
                catch (Exception e) when (e is JsonException || e is ArgumentException)
                {
                    await SendErrorAsync(http, id, Error(-32602, "Invalid params: " + e.Message, "InvalidParams"), 200).ConfigureAwait(false);
                }
                catch (Exception e) when (DatabaseFailures.Classify(e) != DatabaseFailureEnum.None && !http.Response.ResponseSent)
                {
                    http.Response.Headers["Retry-After"] = Math.Max(1, _Server.Settings.Rest.DatabaseRetryAfterSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    await SendErrorAsync(http, id, Error(-32603, "The database is unavailable right now. Retry after the number of seconds in Retry-After.", "DatabaseUnavailable"), 503).ConfigureAwait(false);
                }
            }
            catch (A2AAccessException e)
            {
                await SendErrorAsync(http, id, Error(e.Code, e.Message, e.DataCode), 200).ConfigureAwait(false);
            }
            finally
            {
                if (outcome == "error") AgentTelemetry.SetError(span, "JSON-RPC error");
                AgentTelemetry.Stop(span);
                HugoMetrics.Count(HugoMetricCatalog.A2ARequests, "method", label, "outcome", outcome);
            }
        }

        private async Task SendMessageAsync(HttpContextBase http, RequestContext ctx, string tenantId, AgentProfile profile, object? id, SendMessageRequest request, bool streaming)
        {
            string text = A2AMapper.InputText(request.Message);
            if (String.IsNullOrWhiteSpace(text)) throw new ValidationException("The message has no text or data parts.", new Dictionary<string, string> { { "message.parts", "Required." } });
            if (!String.IsNullOrEmpty(request.Message.TaskId))
            {
                A2ATask existing = await OwnedTaskAsync(ctx, tenantId, profile, request.Message.TaskId!, http.Token).ConfigureAwait(false);
                if (existing.State == A2ATaskStateEnum.InputRequired)
                    throw new A2AAccessException(-32007, "Task " + existing.Id + " waits for approval " + existing.PendingApprovalId + ". A human decides it in the Hugo dashboard or over REST, and the task resumes automatically; it cannot be answered over A2A.", "ApprovalDecidedByHuman");
                if (!existing.IsTerminal()) throw new A2AAccessException(-32007, "Task " + existing.Id + " is still running.", "TurnInProgress");
                throw new A2AAccessException(-32007, "Task " + existing.Id + " already finished; send a new message in its context instead.", "TaskFinished");
            }
            Credential? credential = ctx.Credential;
            if (credential != null && credential.LockToProfile && !String.Equals(credential.AgentProfileId, profile.Id, StringComparison.Ordinal))
                throw new A2AAccessException(-32007, "This credential is locked to another agent profile.", "ProfileLocked");

            A2ATask task = await _Tasks.StartAsync(ctx, tenantId, profile, text, request.Message.ContextId, String.IsNullOrEmpty(request.Message.MessageId) ? null : request.Message.MessageId, http.Token).ConfigureAwait(false);
            if (streaming)
            {
                await StreamAsync(http, id, task).ConfigureAwait(false);
                return;
            }
            if (request.Configuration == null || !request.Configuration.ReturnImmediately)
            {
                task = await _Tasks.WaitAsync(tenantId, task.Id, t => t.IsTerminal() || t.State == A2ATaskStateEnum.InputRequired, TimeSpan.FromSeconds(_SendMessageWaitSeconds), http.Token).ConfigureAwait(false);
            }
            SendMessageResponse response = new SendMessageResponse();
            response.Task = await WireAsync(task, http.Token).ConfigureAwait(false);
            await SendResultAsync(http, id, response).ConfigureAwait(false);
        }

        private async Task ListTasksAsync(HttpContextBase http, RequestContext ctx, string tenantId, AgentProfile profile, object? id, ListTasksRequest request)
        {
            EnumerationQuery query = new EnumerationQuery();
            query.MaxResults = Math.Clamp(request.PageSize ?? 50, 1, 100);
            if (!String.IsNullOrEmpty(request.PageToken) && Int32.TryParse(request.PageToken, out int skip) && skip >= 0) query.Skip = skip;
            A2ATaskStateEnum? state = request.Status.HasValue ? A2AMapper.FromWire(request.Status.Value) : null;
            string? principal = ConversationService.SeesAll(ctx) ? null : (ctx.PrincipalId ?? "none");
            EnumerationResult<A2ATask> page = await _Server.Database.A2ATasks.EnumerateAsync(tenantId, profile.Id, principal, request.ContextId, state, query, http.Token).ConfigureAwait(false);
            ListTasksResponse response = new ListTasksResponse();
            foreach (A2ATask task in page.Objects)
            {
                List<A2ATaskArtifact>? artifacts = request.IncludeArtifacts == true ? await _Server.Database.A2ATasks.ListArtifactsAsync(tenantId, task.Id, http.Token).ConfigureAwait(false) : null;
                response.Tasks.Add(A2AMapper.Task(task, artifacts));
            }
            response.PageSize = query.MaxResults;
            response.TotalSize = (int)Math.Min(page.TotalRecords, Int32.MaxValue);
            response.NextPageToken = page.EndOfResults ? String.Empty : (query.Skip + page.Objects.Count).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await SendResultAsync(http, id, response).ConfigureAwait(false);
        }

        private async Task StreamAsync(HttpContextBase http, object? id, A2ATask task)
        {
            using (SseWriter writer = new SseWriter(http, "a2a"))
            {
                writer.RetryMs = 0;
                writer.Start(false);
                StreamResponse first = new StreamResponse();
                first.Task = A2AMapper.Task(task, null);
                await writer.SendAsync(null, Envelope(id, first), null, http.Token).ConfigureAwait(false);
                A2ATaskStateEnum lastState = task.State;
                string? lastMessage = task.StatusMessage;
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(http.Token, _Server.Stopping))
                {
                    while (!linked.IsCancellationRequested && !writer.Disconnected)
                    {
                        A2ATask? current = await _Server.Database.A2ATasks.ReadAsync(task.TenantId, task.Id, CancellationToken.None).ConfigureAwait(false);
                        if (current == null) return;
                        bool terminal = current.IsTerminal();
                        if (terminal && current.State == A2ATaskStateEnum.Completed)
                        {
                            foreach (A2ATaskArtifact artifact in await _Server.Database.A2ATasks.ListArtifactsAsync(current.TenantId, current.Id, CancellationToken.None).ConfigureAwait(false))
                            {
                                StreamResponse update = new StreamResponse();
                                update.ArtifactUpdate = new TaskArtifactUpdateEvent { TaskId = current.Id, ContextId = current.ContextId, Artifact = A2AMapper.Artifact(artifact), Append = false, LastChunk = true };
                                await writer.SendAsync(null, Envelope(id, update), null, CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                        if (terminal || current.State != lastState || !String.Equals(current.StatusMessage, lastMessage, StringComparison.Ordinal))
                        {
                            StreamResponse status = new StreamResponse();
                            status.StatusUpdate = new TaskStatusUpdateEvent { TaskId = current.Id, ContextId = current.ContextId, Status = A2AMapper.Status(current) };
                            if (terminal)
                            {
                                await writer.CloseAsync(null, Envelope(id, status), CancellationToken.None).ConfigureAwait(false);
                                return;
                            }
                            await writer.SendAsync(null, Envelope(id, status), null, CancellationToken.None).ConfigureAwait(false);
                            lastState = current.State;
                            lastMessage = current.StatusMessage;
                        }
                        try
                        {
                            await _Tasks.Signals.WaitAsync(task.Id, TimeSpan.FromSeconds(1), linked.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                    }
                }
            }
        }

        private async Task<AgentProfile?> ExposedAsync(string tenantId, string? profileName, CancellationToken token)
        {
            if (!_Server.Settings.A2A.Enable || String.IsNullOrEmpty(tenantId) || String.IsNullOrEmpty(profileName)) return null;
            AgentProfile? profile = await _Server.Database.AgentProfiles.ReadByNameAsync(tenantId, profileName, token).ConfigureAwait(false);
            if (profile == null || !profile.Active || !profile.ExposeA2A) return null;
            return profile;
        }

        private async Task<A2ATask> OwnedTaskAsync(RequestContext ctx, string tenantId, AgentProfile profile, string taskId, CancellationToken token)
        {
            A2ATask? task = String.IsNullOrEmpty(taskId) ? null : await _Server.Database.A2ATasks.ReadAsync(tenantId, taskId, token).ConfigureAwait(false);
            bool owner = task != null && !String.IsNullOrEmpty(task.PrincipalId) && String.Equals(task.PrincipalId, ctx.PrincipalId, StringComparison.Ordinal);
            if (task == null || !String.Equals(task.AgentProfileId, profile.Id, StringComparison.Ordinal) || (!owner && !ConversationService.SeesAll(ctx)))
                throw new A2AAccessException((int)A2AErrorCode.TaskNotFound, "Task " + taskId + " not found.", "TaskNotFound");
            return task;
        }

        private async Task RequireAsync(RequestContext ctx, string tenantId, OperationScope? scope, string? resourceId, CancellationToken token)
        {
            if (scope == null || !scope.ResourceType.HasValue || !scope.Operation.HasValue) return;
            AuthorizationDecision decision = await _Server.Authorization.AuthorizeAsync(ctx, tenantId, scope.ResourceType.Value, scope.Operation.Value, resourceId, token).ConfigureAwait(false);
            if (!decision.Permitted) throw new A2AAccessException(-32007, "Not permitted: " + decision.Reason + " (" + scope.Operation.Value + " on " + scope.ResourceType.Value + " is required).", "AuthorizationDenied");
        }

        private async Task<AgentTask> WireAsync(A2ATask task, CancellationToken token)
        {
            List<A2ATaskArtifact> artifacts = await _Server.Database.A2ATasks.ListArtifactsAsync(task.TenantId, task.Id, token).ConfigureAwait(false);
            return A2AMapper.Task(task, artifacts);
        }

        private static T Parse<T>(JsonElement? parameters) where T : new()
        {
            if (!parameters.HasValue || parameters.Value.ValueKind == JsonValueKind.Undefined || parameters.Value.ValueKind == JsonValueKind.Null) return new T();
            return JsonSerializer.Deserialize<T>(parameters.Value.GetRawText(), A2AJson.DefaultOptions) ?? new T();
        }

        private static JsonRpcError Error(int code, string message, string? dataCode)
        {
            JsonRpcError error = new JsonRpcError();
            error.Code = code;
            error.Message = message;
            if (!String.IsNullOrEmpty(dataCode)) error.Data = new A2AErrorData { Code = dataCode };
            return error;
        }

        private static string Envelope(object? id, object result)
        {
            JsonRpcResponse response = new JsonRpcResponse();
            response.Id = id;
            response.Result = result;
            return JsonSerializer.Serialize(response, A2AJson.DefaultOptions);
        }

        private static async Task SendJsonAsync(HttpContextBase http, int status, object body)
        {
            http.Response.StatusCode = status;
            http.Response.ContentType = "application/json";
            await http.Response.Send(JsonSerializer.Serialize(body, body.GetType(), A2AJson.DefaultOptions), http.Token).ConfigureAwait(false);
        }

        private static async Task SendResultAsync(HttpContextBase http, object? id, object result)
        {
            JsonRpcResponse response = new JsonRpcResponse();
            response.Id = id;
            response.Result = result;
            await SendJsonAsync(http, 200, response).ConfigureAwait(false);
        }

        private static async Task SendErrorAsync(HttpContextBase http, object? id, JsonRpcError error, int status)
        {
            if (http.Response.ResponseSent) return;
            JsonRpcResponse response = new JsonRpcResponse();
            response.Id = id;
            response.Error = error;
            await SendJsonAsync(http, status, response).ConfigureAwait(false);
        }

        #endregion
    }
}
