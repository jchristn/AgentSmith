namespace Hugo.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>Read-only management of submitted conversation feedback with associated persisted turn telemetry.</summary>
    public class FeedbackRoutes
    {
        private readonly ServerContext _Server;
        private readonly ConversationService _Conversations;
        private RouteRegistrar? _Routes;

        /// <summary>Construct feedback management routes.</summary>
        /// <param name="server">Server context.</param>
        /// <param name="conversations">Conversation visibility service.</param>
        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        public FeedbackRoutes(ServerContext server, ConversationService conversations)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        }

        /// <summary>Register tenant-scoped feedback review routes.</summary>
        /// <param name="routes">Route registrar.</param>
        /// <exception cref="ArgumentNullException">The registrar is null.</exception>
        public void Register(RouteRegistrar routes)
        {
            _Routes = routes ?? throw new ArgumentNullException(nameof(routes));
            routes.Scoped(HttpMethod.GET, "/v1.0/api/feedback", ResourceTypeEnum.Conversation, OperationTypeEnum.Read, ListAsync, "List submitted feedback for agentProfileId, newest first, with request, response and persisted turn telemetry", "Feedback", null, typeof(EnumerationResult<FeedbackReview>), "none");
            routes.Scoped(HttpMethod.GET, "/v1.0/api/feedback/{messageId}", ResourceTypeEnum.Conversation, OperationTypeEnum.Read, ReadAsync, "Read submitted feedback and associated persisted turn telemetry for an assistant message", "Feedback", null, typeof(FeedbackReview), "none");
        }

        private async Task ListAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            if (ctx.IsPublicChat) throw new ForbiddenException("PublicChatRestricted", "Public chat visitors cannot review feedback.");
            string? profileId = RouteHelpers.Query(http, "agentProfileId");
            if (String.IsNullOrWhiteSpace(profileId))
            {
                await RouteHelpers.ErrorAsync(http, 400, "BadRequest", "agentProfileId is required.").ConfigureAwait(false);
                return;
            }
            if (!await RequireProfileAsync(http, profileId).ConfigureAwait(false)) return;
            EnumerationResult<FeedbackReview> page = await QueryAsync(http, profileId, null, RouteHelpers.Enumeration(http)).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, page).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            if (ctx.IsPublicChat) throw new ForbiddenException("PublicChatRestricted", "Public chat visitors cannot review feedback.");
            string tenant = RouteHelpers.Tenant(http);
            ConversationMessage? message = await _Server.Database.Messages.ReadAsync(tenant, RouteHelpers.Param(http, "messageId")!, http.Token).ConfigureAwait(false);
            if (message == null) throw new NotFoundException("Feedback not found.");
            Conversation conversation = await _Conversations.ReadAsync(ctx, tenant, message.ConversationId, http.Token).ConfigureAwait(false);
            if (!await _Routes!.RequireAsync(http, tenant, ResourceTypeEnum.Conversation, OperationTypeEnum.Read, conversation.Id).ConfigureAwait(false)) return;
            if (!await RequireProfileAsync(http, conversation.AgentProfileId).ConfigureAwait(false)) return;
            EnumerationResult<FeedbackReview> page = await QueryAsync(http, conversation.AgentProfileId, message.Id, new EnumerationQuery { MaxResults = 1 }).ConfigureAwait(false);
            if (page.Objects.Count == 0) throw new NotFoundException("Feedback not found.");
            await RouteHelpers.JsonAsync(http, 200, page.Objects[0]).ConfigureAwait(false);
        }

        private async Task<bool> RequireProfileAsync(HttpContextBase http, string profileId)
        {
            string tenant = RouteHelpers.Tenant(http);
            RequestContext ctx = RouteHelpers.Context(http);
            AgentProfile? profile = await _Server.Database.AgentProfiles.ReadAsync(tenant, profileId, http.Token).ConfigureAwait(false);
            if (profile == null || !ResourceVisibility.CanSee(profile, ctx)) throw new NotFoundException("Agent profile not found.");
            return await _Routes!.RequireAsync(http, tenant, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, profile.Id).ConfigureAwait(false);
        }

        private async Task<EnumerationResult<FeedbackReview>> QueryAsync(HttpContextBase http, string profileId, string? messageId, EnumerationQuery query)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            string? userId = null;
            string? credentialId = null;
            if (!ConversationService.SeesAll(ctx))
            {
                if (ctx.PrincipalType == PrincipalTypeEnum.Credential) credentialId = ctx.CredentialId ?? "none";
                else userId = ctx.UserId ?? "none";
            }
            return await _Server.Database.Messages.EnumerateFeedbackAsync(RouteHelpers.Tenant(http), profileId, userId, credentialId, messageId, query, http.Token).ConfigureAwait(false);
        }
    }
}
