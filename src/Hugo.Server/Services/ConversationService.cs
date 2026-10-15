namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;
    using Hugo.Core.Services;

    /// <summary>
    /// Conversation management with the visibility rule of plan 13.4 and P6.24: callers see their own conversations
    /// (a user sees every conversation they own, including ones their credentials started; a credential sees the ones it
    /// started), and tenant administrators see every conversation in the tenant. A conversation the caller cannot see is
    /// reported as not found. Thread-safe.
    /// </summary>
    public class ConversationService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly ActiveTurnRegistry _Turns;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="turns">Running turns.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ConversationService(DatabaseDriverBase database, ActiveTurnRegistry turns)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Turns = turns ?? throw new ArgumentNullException(nameof(turns));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the caller may see every conversation in the tenant.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <returns>True for system and tenant administrators.</returns>
        public static bool SeesAll(RequestContext ctx)
        {
            if (ctx == null) return false;
            return ctx.IsAdmin || ctx.IsTenantAdmin || ctx.PrincipalType == PrincipalTypeEnum.Administrator;
        }

        /// <summary>
        /// Whether the caller may see a conversation.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="conversation">Conversation.</param>
        /// <returns>True when visible.</returns>
        public static bool CanSee(RequestContext ctx, Conversation conversation)
        {
            if (ctx == null || conversation == null) return false;
            if (SeesAll(ctx)) return true;
            if (ctx.PrincipalType == PrincipalTypeEnum.Credential) return !String.IsNullOrEmpty(ctx.CredentialId) && String.Equals(conversation.CredentialId, ctx.CredentialId, StringComparison.Ordinal);
            return !String.IsNullOrEmpty(ctx.UserId) && String.Equals(conversation.UserId, ctx.UserId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Create a conversation owned by the caller.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profile">Agent profile.</param>
        /// <param name="source">Source surface.</param>
        /// <param name="title">Title, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public async Task<Conversation> CreateAsync(RequestContext ctx, string tenantId, AgentProfile profile, ConversationSourceEnum source, string? title, CancellationToken token = default)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            Conversation conversation = New(ctx, tenantId, profile, source);
            conversation.Title = String.IsNullOrWhiteSpace(title) ? null : title.Trim();
            return await _Database.Conversations.CreateAsync(conversation, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Build (without storing) a conversation owned by the caller.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profile">Agent profile.</param>
        /// <param name="source">Source surface.</param>
        /// <returns>Conversation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static Conversation New(RequestContext ctx, string tenantId, AgentProfile profile, ConversationSourceEnum source)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            Conversation conversation = new Conversation();
            conversation.TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
            conversation.UserId = ctx.UserId;
            conversation.CredentialId = ctx.PrincipalType == PrincipalTypeEnum.Credential ? ctx.CredentialId : null;
            conversation.AgentProfileId = profile.Id;
            conversation.Source = source;
            return conversation;
        }

        /// <summary>
        /// Read a conversation the caller can see.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversation.</returns>
        /// <exception cref="NotFoundException">Thrown when it does not exist or is not visible.</exception>
        public async Task<Conversation> ReadAsync(RequestContext ctx, string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new NotFoundException("Conversation not found.");
            Conversation? conversation = await _Database.Conversations.ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (conversation == null || !CanSee(ctx, conversation)) throw new NotFoundException("Conversation not found.");
            return conversation;
        }

        /// <summary>
        /// List the conversations the caller can see.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="query">Paging and title search.</param>
        /// <param name="filter">Filters; ownership filters are replaced for callers who do not see everything.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<Conversation>> ListAsync(RequestContext ctx, string tenantId, EnumerationQuery query, ConversationFilter filter, CancellationToken token = default)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (filter == null) filter = new ConversationFilter();
            if (!SeesAll(ctx))
            {
                if (ctx.PrincipalType == PrincipalTypeEnum.Credential)
                {
                    filter.CredentialId = ctx.CredentialId ?? "none";
                    filter.UserId = null;
                }
                else
                {
                    filter.UserId = ctx.UserId ?? "none";
                    filter.CredentialId = null;
                }
            }
            return await _Database.Conversations.EnumerateAsync(tenantId, query, filter, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Rename a conversation.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="request">Request (title).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversation.</returns>
        /// <exception cref="NotFoundException">Thrown when not visible.</exception>
        /// <exception cref="ValidationException">Thrown when the title is empty.</exception>
        public async Task<Conversation> UpdateAsync(RequestContext ctx, string tenantId, string id, ConversationRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            Conversation conversation = await ReadAsync(ctx, tenantId, id, token).ConfigureAwait(false);
            if (request.Title != null)
            {
                if (String.IsNullOrWhiteSpace(request.Title)) throw new ValidationException("The title cannot be empty.", new Dictionary<string, string> { { "title", "Required." } });
                conversation.Title = request.Title.Trim();
            }
            return await _Database.Conversations.UpdateAsync(conversation, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a conversation and its messages.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="NotFoundException">Thrown when not visible.</exception>
        /// <exception cref="ConflictException">Thrown when a turn is running (code TurnInProgress).</exception>
        public async Task DeleteAsync(RequestContext ctx, string tenantId, string id, CancellationToken token = default)
        {
            Conversation conversation = await ReadAsync(ctx, tenantId, id, token).ConfigureAwait(false);
            if (_Turns.IsActive(conversation.Id)) throw new ConflictException("TurnInProgress", "A turn is running in conversation " + id + ". Cancel it before deleting the conversation.");
            await _Database.Conversations.DeleteAsync(tenantId, conversation.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Export a conversation as Markdown.
        /// </summary>
        /// <param name="conversation">Conversation.</param>
        /// <param name="profile">Its profile, or null when it was deleted.</param>
        /// <param name="messages">Messages in order (compacted included).</param>
        /// <returns>Markdown.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static string ExportMarkdown(Conversation conversation, AgentProfile? profile, List<ConversationMessage> messages)
        {
            if (conversation == null) throw new ArgumentNullException(nameof(conversation));
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            string persona = profile?.PersonaName ?? "Assistant";
            string address = profile?.UserAddress ?? "User";
            StringBuilder md = new StringBuilder();
            md.Append("# ").AppendLine(String.IsNullOrEmpty(conversation.Title) ? "Conversation " + conversation.Id : conversation.Title).AppendLine();
            md.Append("- Conversation: `").Append(conversation.Id).AppendLine("`");
            md.Append("- Profile: ").AppendLine(profile == null ? "`" + conversation.AgentProfileId + "`" : profile.Name);
            md.Append("- Source: ").AppendLine(conversation.Source.ToString());
            md.Append("- Started: ").AppendLine(conversation.CreatedUtc.ToString("u"));
            md.Append("- Tokens: ").Append(conversation.TotalInputTokens).Append(" in, ").Append(conversation.TotalOutputTokens).AppendLine(" out").AppendLine();
            foreach (ConversationMessage m in messages)
            {
                switch (m.Role)
                {
                    case AgentMessageRoleEnum.User:
                        md.Append("## ").AppendLine(address).AppendLine().AppendLine(m.Content).AppendLine();
                        break;
                    case AgentMessageRoleEnum.Assistant:
                        md.Append("## ").AppendLine(persona).AppendLine();
                        if (!String.IsNullOrWhiteSpace(m.Content)) md.AppendLine(m.Content).AppendLine();
                        foreach (ConversationToolCall call in m.ToolCalls)
                        {
                            md.Append("- Tool call `").Append(call.Name).Append("`: `").Append(call.ArgumentsJson.Replace("`", "'")).AppendLine("`");
                        }
                        if (m.ToolCalls.Count > 0) md.AppendLine();
                        break;
                    case AgentMessageRoleEnum.Tool:
                        md.Append("### Tool result: ").AppendLine(m.ToolName ?? "tool").AppendLine();
                        md.AppendLine("```").AppendLine(m.Content.Replace("```", "'''")).AppendLine("```").AppendLine();
                        break;
                    case AgentMessageRoleEnum.Summary:
                        md.AppendLine("## Summary of earlier messages").AppendLine().AppendLine(m.Content).AppendLine();
                        break;
                    default:
                        md.AppendLine("## Note").AppendLine().AppendLine(m.Content).AppendLine();
                        break;
                }
            }
            return md.ToString();
        }

        /// <summary>
        /// Export a conversation as JSON: the conversation and its messages.
        /// </summary>
        /// <param name="conversation">Conversation.</param>
        /// <param name="messages">Messages in order (compacted included).</param>
        /// <returns>JSON.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static string ExportJson(Conversation conversation, List<ConversationMessage> messages)
        {
            if (conversation == null) throw new ArgumentNullException(nameof(conversation));
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            ConversationExport export = new ConversationExport();
            export.Conversation = conversation;
            export.Messages = messages.ToList();
            return Serializer.SerializeReadable(export);
        }

        #endregion
    }
}
