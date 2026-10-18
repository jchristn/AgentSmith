namespace Hugo.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Narrow anonymous entry points for standalone chat links. Authentication and capability checks are applied
    /// explicitly, and only safe text DTOs are returned. No transcript enumeration or raw event stream exists.
    /// </summary>
    public class StandaloneChatRoutes
    {
        private const string _Base = "/v1.0/chat/{linkId}";
        private readonly ServerContext _Server;
        private readonly StandaloneChatService _Chat;

        /// <summary>Instantiate the routes.</summary>
        /// <param name="server">Server context.</param>
        /// <param name="chat">Standalone service.</param>
        public StandaloneChatRoutes(ServerContext server, StandaloneChatService chat)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Chat = chat ?? throw new ArgumentNullException(nameof(chat));
        }

        /// <summary>Register the limited public surface.</summary>
        /// <param name="routes">Registrar.</param>
        public void Register(RouteRegistrar routes)
        {
            routes.Anonymous(HttpMethod.GET, _Base, InfoAsync, "Read safe standalone chat link presentation", "Standalone chat", response: typeof(StandaloneChatInfo));
            routes.Anonymous(HttpMethod.POST, _Base + "/conversations", CreateAsync, "Start an isolated standalone chat conversation", "Standalone chat", response: typeof(StandaloneChatSession), statuses: new[] { 201 });
            routes.Anonymous(HttpMethod.GET, _Base + "/conversations/{id}/messages", MessagesAsync, "Read this visitor's safe chat messages", "Standalone chat", response: typeof(StandaloneChatMessages));
            routes.Anonymous(HttpMethod.POST, _Base + "/conversations/{id}/messages", SendAsync, "Run a standalone chat turn and return safe final text", "Standalone chat", typeof(StandaloneChatMessageRequest), typeof(StandaloneChatTurn));
            routes.Anonymous(HttpMethod.POST, _Base + "/conversations/{id}/messages/{messageId}/feedback", FeedbackAsync, "Update feedback on this visitor's assistant message", "Standalone chat", typeof(StandaloneChatFeedbackRequest), typeof(StandaloneChatFeedback));
            routes.Anonymous(HttpMethod.POST, _Base + "/conversations/{id}/cancel", CancelAsync, "Cancel this visitor's own running turn", "Standalone chat", response: typeof(StandaloneChatCancel));
        }

        private Task InfoAsync(HttpContextBase http)
        {
            return RespondAsync(http, async () => await _Chat.InfoAsync(Link(http), http.Token).ConfigureAwait(false));
        }

        private Task CreateAsync(HttpContextBase http)
        {
            return RespondAsync(http, async () =>
            {
                StandaloneChatAccess access = await AccessAsync(http).ConfigureAwait(false);
                await RateAsync(http).ConfigureAwait(false);
                access.Principal.SuppressHistoryBodies = true;
                return await _Chat.CreateAsync(access, http.Token).ConfigureAwait(false);
            }, 201);
        }

        private Task MessagesAsync(HttpContextBase http)
        {
            return RespondAsync(http, async () => await _Chat.MessagesAsync(await AccessAsync(http).ConfigureAwait(false), Id(http), Capability(http), http.Token).ConfigureAwait(false));
        }

        private Task SendAsync(HttpContextBase http)
        {
            return RespondAsync(http, async () =>
            {
                StandaloneChatAccess access = await AccessAsync(http).ConfigureAwait(false);
                await RateAsync(http).ConfigureAwait(false);
                StandaloneChatMessageRequest request = RouteHelpers.RequiredBody<StandaloneChatMessageRequest>(http);
                string? accept = http.Request.Headers["Accept"];
                if (String.IsNullOrEmpty(accept) || !accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
                    return await _Chat.SendAsync(access, Id(http), Capability(http), request.Message, http.Token).ConfigureAwait(false);
                access.Principal.SuppressHistoryBodies = true;
                using (SseWriter writer = new SseWriter(http, "standalone-chat"))
                {
                    try
                    {
                        StandaloneChatEventSink sink = new StandaloneChatEventSink(writer, _Chat, access, Id(http));
                        StandaloneChatTurn result = await _Chat.SendAsync(access, Id(http), Capability(http), request.Message, System.Threading.CancellationToken.None, sink).ConfigureAwait(false);
                        await writer.CloseAsync("turn.completed", Serializer.Serialize(result), http.Token).ConfigureAwait(false);
                    }
                    catch (Exception) when (writer.EventsWritten > 0)
                    {
                        await writer.CloseAsync("error", Serializer.Serialize(new { code = "chat_failed", message = "Chat could not complete. Try again or contact the link publisher." }), System.Threading.CancellationToken.None).ConfigureAwait(false);
                    }
                }
                return new object();
            });
        }

        private Task FeedbackAsync(HttpContextBase http)
        {
            return RespondAsync(http, async () =>
            {
                StandaloneChatAccess access = await AccessAsync(http).ConfigureAwait(false);
                await RateAsync(http).ConfigureAwait(false);
                StandaloneChatFeedbackRequest request = RouteHelpers.RequiredBody<StandaloneChatFeedbackRequest>(http);
                return await _Chat.FeedbackAsync(access, Id(http), Capability(http), RouteHelpers.Param(http, "messageId") ?? String.Empty, request, http.Token).ConfigureAwait(false);
            });
        }

        private Task CancelAsync(HttpContextBase http)
        {
            return RespondAsync(http, async () => await _Chat.CancelAsync(await AccessAsync(http).ConfigureAwait(false), Id(http), Capability(http), http.Token).ConfigureAwait(false));
        }

        private async Task<StandaloneChatAccess> AccessAsync(HttpContextBase http)
        {
            RequestContext? visitor = null;
            string? authorization = http.Request.Headers["Authorization"];
            if (!String.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                visitor = await _Server.Authentication.ResolveAsync(http.Request.Headers, http.Request.Method.ToString(), http.Request.Url.RawWithoutQuery, null, http.Request.DataAsBytes, http.Request.Source.IpAddress, http.Token).ConfigureAwait(false);
            }
            StandaloneChatAccess access = await _Chat.ResolveAsync(Link(http), visitor, http.Token).ConfigureAwait(false);
            access.Principal.SourceIp = http.Request.Source.IpAddress;
            http.Metadata = access.Principal;
            return access;
        }

        private async Task RateAsync(HttpContextBase http)
        {
            AuthRateLimiter limiter = new AuthRateLimiter(_Server.Database, () => _Server.Settings.Auth.RateLimitPerMinute) { Scope = "standalone-chat-" + Link(http) };
            if (!await limiter.TryAcquireAsync(http.Request.Source.IpAddress, http.Token).ConfigureAwait(false))
                throw new ForbiddenException("StandaloneRateLimit", "Too many chat requests. Wait before trying again.");
        }

        private static async Task RespondAsync(HttpContextBase http, Func<Task<object>> action, int status = 200)
        {
            http.Response.Headers.Add("Cache-Control", "no-store");
            try
            {
                object response = await action().ConfigureAwait(false);
                if (!http.Response.ServerSentEvents) await RouteHelpers.JsonAsync(http, status, response).ConfigureAwait(false);
            }
            catch (NotFoundException)
            {
                await ErrorAsync(http, 404, "not_found", "Chat link or conversation not found.").ConfigureAwait(false);
            }
            catch (ForbiddenException error)
            {
                int code = error.Code == "AuthenticationRequired" ? 401 : error.Code == "StandaloneRateLimit" ? 429 : 403;
                if (code == 429) http.Response.Headers.Add("Retry-After", "60");
                await ErrorAsync(http, code, error.Code, error.Message).ConfigureAwait(false);
            }
            catch (ValidationException error)
            {
                await ErrorAsync(http, 400, "invalid_argument", error.Message).ConfigureAwait(false);
            }
            catch (ConflictException)
            {
                await ErrorAsync(http, 409, "conflict", "A turn is already active or this conversation changed.").ConfigureAwait(false);
            }
            catch (AgentTurnConflictException)
            {
                await ErrorAsync(http, 409, "conflict", "A turn is already active in this conversation.").ConfigureAwait(false);
            }
            catch (RequestBodyException)
            {
                await ErrorAsync(http, 400, "invalid_argument", "A valid JSON message body is required.").ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (http.Token.IsCancellationRequested)
            {
                // The visitor disconnected; do not attempt to disclose or serialize internal state.
            }
            catch (Exception)
            {
                await ErrorAsync(http, 500, "chat_failed", "Chat could not complete. Try again or contact the link publisher.").ConfigureAwait(false);
            }
        }

        private static Task ErrorAsync(HttpContextBase http, int status, string code, string message)
        {
            return RouteHelpers.JsonAsync(http, status, new ErrorResponse("StandaloneChat", code, message));
        }

        private static string Link(HttpContextBase http)
        {
            return RouteHelpers.Param(http, "linkId") ?? String.Empty;
        }

        private static string Id(HttpContextBase http)
        {
            return RouteHelpers.Param(http, "id") ?? String.Empty;
        }

        private static string? Capability(HttpContextBase http)
        {
            return http.Request.Headers["x-hugo-chat-session"];
        }
    }
}
