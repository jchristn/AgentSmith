namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;
    using Hugo.Core.Services;

    /// <summary>
    /// Scoped standalone conversations. Every request resolves fresh link and identity state; public visitors receive
    /// only assistant text and usage. Conversation capabilities are stored only as hashes. Thread-safe.
    /// </summary>
    public class StandaloneChatService
    {
        private readonly ServerContext _Server;
        private readonly AgentTurnHost _Host;

        /// <summary>Instantiate the standalone service.</summary>
        /// <param name="server">Server context.</param>
        /// <param name="host">Normal agent turn pipeline.</param>
        public StandaloneChatService(ServerContext server, AgentTurnHost host)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>Read minimal link presentation metadata.</summary>
        /// <param name="linkId">Opaque link.</param>
        /// <param name="token">Cancellation.</param>
        /// <returns>Visitor-safe metadata.</returns>
        public async Task<StandaloneChatInfo> InfoAsync(string linkId, CancellationToken token = default)
        {
            AgentProfile profile = await ProfileAsync(linkId, token).ConfigureAwait(false);
            if (profile.ChatAccessMode == ChatAccessModeEnum.Public) await ResolveAsync(linkId, null, token).ConfigureAwait(false);
            return new StandaloneChatInfo { Name = profile.Name, DisplayName = profile.DisplayName, AccessMode = profile.ChatAccessMode, AuthenticationRequired = profile.ChatAccessMode == ChatAccessModeEnum.Authenticated };
        }

        /// <summary>Resolve fresh link permissions and visitor identity before any operation.</summary>
        /// <param name="linkId">Opaque link.</param>
        /// <param name="visitor">Bearer-authenticated visitor for authenticated mode, otherwise ignored.</param>
        /// <param name="token">Cancellation.</param>
        /// <returns>Request-local access, never a visitor response.</returns>
        public async Task<StandaloneChatAccess> ResolveAsync(string linkId, RequestContext? visitor, CancellationToken token = default)
        {
            AgentProfile profile = await ProfileAsync(linkId, token).ConfigureAwait(false);
            RequestContext principal;
            if (profile.ChatAccessMode == ChatAccessModeEnum.Public)
            {
                Credential? stored = String.IsNullOrEmpty(profile.ChatCredentialId) ? null : await _Server.Database.Credentials.ReadAsync(profile.TenantId, profile.ChatCredentialId, token).ConfigureAwait(false);
                if (stored == null || !stored.Active || stored.ExpiresUtc <= DateTime.UtcNow || stored.AuthMode == CredentialAuthModeEnum.SessionExchange || (stored.LockToProfile && stored.AgentProfileId != profile.Id)) throw Missing();
                User? owner = await _Server.Database.Users.ReadAsync(profile.TenantId, stored.UserId, token).ConfigureAwait(false);
                if (owner == null || !owner.Active) throw Missing();
                Credential credential = Serializer.Copy(stored) ?? throw Missing();
                credential.InheritOwnerBypass = false;
                credential.AgentProfileId = profile.Id;
                credential.LockToProfile = true;
                principal = new RequestContext
                {
                    IsAuthenticated = true, AuthResult = AuthResultEnum.Success, AuthScheme = AuthSchemeEnum.AccessKeySecret,
                    PrincipalType = PrincipalTypeEnum.Credential, PrincipalName = "Standalone chat visitor", TenantId = profile.TenantId,
                    UserId = owner.Id, CredentialId = credential.Id, User = owner, Credential = credential,
                    IsAdmin = false, IsTenantAdmin = false, IsPublicChat = true, PublicChatProfileId = profile.Id,
                    PublicChatRevision = profile.ChatRevision, SuppressHistoryBodies = true
                };
            }
            else
            {
                if (visitor == null || !visitor.IsAuthenticated) throw new ForbiddenException("AuthenticationRequired", "A valid bearer session is required for this chat link.");
                principal = visitor;
                if ((!principal.IsAdmin && principal.PrincipalType != PrincipalTypeEnum.Administrator && principal.TenantId != profile.TenantId) || !ResourceVisibility.CanSee(profile, principal)) throw Missing();
                if (principal.Credential?.LockToProfile == true && principal.Credential.AgentProfileId != profile.Id) throw Missing();
            }
            await DemandAsync(principal, profile, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute, token).ConfigureAwait(false);
            return new StandaloneChatAccess { Profile = profile, Principal = principal };
        }

        /// <summary>Create an isolated visitor-owned conversation and return its capability once.</summary>
        /// <param name="access">Fresh resolved access.</param>
        /// <param name="token">Cancellation.</param>
        /// <returns>Conversation ID and secret capability.</returns>
        public async Task<StandaloneChatSession> CreateAsync(StandaloneChatAccess access, CancellationToken token = default)
        {
            await DemandAsync(access.Principal, access.Profile, ResourceTypeEnum.Conversation, OperationTypeEnum.Create, token).ConfigureAwait(false);
            string capability = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            Conversation conversation = new Conversation
            {
                TenantId = access.Profile.TenantId, AgentProfileId = access.Profile.Id, UserId = access.Principal.UserId,
                CredentialId = access.Principal.CredentialId, Source = ConversationSourceEnum.Rest, Title = "Standalone chat",
                ExternalKey = CapabilityKey(access.Profile, capability)
            };
            await _Server.Database.Conversations.CreateAsync(conversation, token).ConfigureAwait(false);
            return new StandaloneChatSession { Id = conversation.Id, SessionToken = capability };
        }

        /// <summary>Return only user and assistant text from this visitor's conversation.</summary>
        /// <param name="access">Fresh access.</param>
        /// <param name="id">Conversation.</param>
        /// <param name="capability">Visitor session header.</param>
        /// <param name="token">Cancellation.</param>
        /// <returns>Safe transcript, with no raw tools or thinking.</returns>
        public async Task<StandaloneChatMessages> MessagesAsync(StandaloneChatAccess access, string id, string? capability, CancellationToken token = default)
        {
            Conversation conversation = await ConversationAsync(access, id, capability, OperationTypeEnum.Read, token).ConfigureAwait(false);
            List<ConversationMessage> messages = await _Server.Database.Messages.ListAsync(conversation.TenantId, conversation.Id, false, token).ConfigureAwait(false);
            return new StandaloneChatMessages { Messages = messages.Where(m => (m.Role == AgentMessageRoleEnum.User || (m.Role == AgentMessageRoleEnum.Assistant && m.ToolCalls.Count == 0)) && !String.IsNullOrEmpty(m.Content)).Select(m => new StandaloneChatMessage
            {
                Role = m.Role == AgentMessageRoleEnum.User ? "user" : "assistant", Content = SafeText(m.Content, m.FinishReason),
                CreatedUtc = m.CreatedUtc, InputTokens = m.InputTokens, OutputTokens = m.OutputTokens
            }).ToList() };
        }

        /// <summary>Run the actual normal agent pipeline and expose only its final answer and usage.</summary>
        /// <param name="access">Fresh access.</param>
        /// <param name="id">Conversation.</param>
        /// <param name="capability">Visitor capability.</param>
        /// <param name="message">User input, at most 16384 characters.</param>
        /// <param name="token">Cancellation.</param>
        /// <returns>Safe final response.</returns>
        public async Task<StandaloneChatTurn> SendAsync(StandaloneChatAccess access, string id, string? capability, string message, CancellationToken token = default)
        {
            Conversation conversation = await ConversationAsync(access, id, capability, OperationTypeEnum.Update, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(message) || message.Length > 16384) throw new ValidationException("A message of 1 through 16384 characters is required.", new Dictionary<string, string> { { "message", "A message of 1 through 16384 characters is required." } });
            AgentTurnResult result = await _Host.RunAsync(new AgentTurnOptions
            {
                Principal = access.Principal, TenantId = access.Profile.TenantId, Profile = access.Profile, Conversation = conversation,
                UserMessage = message, Surface = InvocationSurfaceEnum.Chat, WaitMode = ApprovalWaitModeEnum.NoWait, GenerateTitle = false
            }, token).ConfigureAwait(false);
            AgentProfile current = await ProfileAsync(access.Profile.ChatLinkId ?? String.Empty, token).ConfigureAwait(false);
            if (current.ChatRevision != access.Profile.ChatRevision || current.ChatAccessMode != access.Profile.ChatAccessMode || current.ChatCredentialId != access.Profile.ChatCredentialId)
                throw Missing();
            await DemandAsync(access.Principal, current, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute, token).ConfigureAwait(false);
            // Do not surface internal error messages, tool records, arguments, or hidden reasoning.
            return new StandaloneChatTurn { ConversationId = id, Text = SafeText(result.FinalText, result.FinishReason.ToString()), Status = result.FinishReason.ToString(), TokensIn = result.TokensIn, TokensOut = result.TokensOut };
        }

        /// <summary>Cancel only the capability holder's own turn.</summary>
        /// <param name="access">Fresh access.</param>
        /// <param name="id">Conversation.</param>
        /// <param name="capability">Visitor capability.</param>
        /// <param name="token">Cancellation.</param>
        /// <returns>Whether a running turn was cancelled.</returns>
        public async Task<StandaloneChatCancel> CancelAsync(StandaloneChatAccess access, string id, string? capability, CancellationToken token = default)
        {
            await ConversationAsync(access, id, capability, OperationTypeEnum.Update, token).ConfigureAwait(false);
            return new StandaloneChatCancel { Cancelled = _Host.Turns.Cancel(id) };
        }

        private async Task<AgentProfile> ProfileAsync(string linkId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(linkId) || linkId.Length > 64) throw Missing();
            AgentProfile? profile = await _Server.Database.AgentProfiles.ReadByChatLinkAsync(linkId, token).ConfigureAwait(false);
            if (profile == null || !profile.ChatEnabled || !profile.Active || !Enum.IsDefined(profile.ChatAccessMode)) throw Missing();
            Tenant? tenant = await _Server.Database.Tenants.ReadAsync(profile.TenantId, token).ConfigureAwait(false);
            if (tenant == null || !tenant.Active) throw Missing();
            return profile;
        }

        private async Task<Conversation> ConversationAsync(StandaloneChatAccess access, string id, string? capability, OperationTypeEnum operation, CancellationToken token)
        {
            if (String.IsNullOrEmpty(capability) || capability.Length != 64) throw Missing();
            Conversation? conversation = await _Server.Database.Conversations.ReadAsync(access.Profile.TenantId, id, token).ConfigureAwait(false);
            string expected = CapabilityKey(access.Profile, capability);
            if (conversation == null || conversation.AgentProfileId != access.Profile.Id || conversation.UserId != access.Principal.UserId || conversation.CredentialId != access.Principal.CredentialId
                || !PasswordHasher.FixedTimeEquals(conversation.ExternalKey ?? String.Empty, expected)) throw Missing();
            await DemandAsync(access.Principal, access.Profile, ResourceTypeEnum.Conversation, operation, token).ConfigureAwait(false);
            return conversation;
        }

        private async Task DemandAsync(RequestContext principal, AgentProfile profile, ResourceTypeEnum type, OperationTypeEnum operation, CancellationToken token)
        {
            AuthorizationDecision decision = await _Server.Authorization.AuthorizeAsync(principal, profile.TenantId, type, operation, type == ResourceTypeEnum.AgentProfile ? profile.Id : null, token).ConfigureAwait(false);
            if (!decision.Permitted) throw new ForbiddenException("permission_denied", "This chat identity is not allowed to perform the requested operation.");
        }

        private static string SafeText(string text, string? status)
        {
            if (String.Equals(status, "Error", StringComparison.OrdinalIgnoreCase)) return "Chat could not complete. Try again or contact the link publisher.";
            if (String.Equals(status, "ApprovalPending", StringComparison.OrdinalIgnoreCase)) return "This request needs approval from an authorized operator before it can continue.";
            if (String.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)) return "The request was cancelled.";
            return SecretScrubber.Shared.Scrub(text) ?? String.Empty;
        }

        private static string CapabilityKey(AgentProfile profile, string capability)
        {
            return "standalone:" + profile.ChatAccessMode + ":" + profile.ChatLinkId + ":" + profile.ChatRevision + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(capability)));
        }

        private static NotFoundException Missing()
        {
            return new NotFoundException("Chat link or conversation not found.");
        }
    }
}
