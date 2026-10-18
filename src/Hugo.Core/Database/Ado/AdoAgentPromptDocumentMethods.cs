namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Services;

    /// <summary>Atomic tenant-qualified attachment mutations with profile-set concurrency. Thread-safe.</summary>
    public class AdoAgentPromptDocumentMethods : IAgentPromptDocumentMethods
    {
        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private int _MaxAttachmentsPerProfile = 32;

        /// <inheritdoc />
        public int MaxAttachmentsPerProfile { get { return Volatile.Read(ref _MaxAttachmentsPerProfile); } set { if (value < 1 || value > 128) throw new ArgumentOutOfRangeException(nameof(value)); Volatile.Write(ref _MaxAttachmentsPerProfile, value); } }

        /// <summary>Initialize attachment persistence.</summary>
        /// <param name="driver">Owning database.</param>
        public AdoAgentPromptDocumentMethods(AdoDatabaseDriver driver) { _Driver = driver ?? throw new ArgumentNullException(nameof(driver)); _Table = driver.Table("agent_prompt_documents"); }

        /// <inheritdoc />
        public Task<List<AgentPromptDocument>> ListAsync(string tenantId, string profileId, CancellationToken token = default)
        {
            AdoPromptDocumentMethods.Scope(tenantId, profileId);
            return _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE tenant_id=@tenant AND agent_profile_id=@id ORDER BY display_order, id;", command => AdoPromptDocumentMethods.BindScope(command, tenantId, profileId), PromptDocumentSql.MapAgentPromptDocument, token);
        }

        /// <inheritdoc />
        public Task<AgentPromptDocument?> ReadAsync(string tenantId, string profileId, string id, CancellationToken token = default)
        {
            AdoPromptDocumentMethods.Scope(tenantId, profileId);
            return _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE tenant_id=@tenant AND agent_profile_id=@profile AND id=@id;", command => { AdoPromptDocumentMethods.BindScope(command, tenantId, id); AdoDatabaseDriver.Add(command, "profile", profileId); }, PromptDocumentSql.MapAgentPromptDocument, token);
        }

        /// <inheritdoc />
        public Task<List<AgentPromptDocument>> ReferrersAsync(string tenantId, string documentId, CancellationToken token = default)
        {
            AdoPromptDocumentMethods.Scope(tenantId, documentId);
            return _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE tenant_id=@tenant AND document_id=@id ORDER BY agent_profile_id, display_order, id;", command => AdoPromptDocumentMethods.BindScope(command, tenantId, documentId), PromptDocumentSql.MapAgentPromptDocument, token);
        }

        /// <inheritdoc />
        public async Task<AgentPromptDocument> CreateAsync(AgentPromptDocument attachment, long expectedSetVersion, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            Validate(attachment);
            AdoPromptDocumentMethods.Version(expectedSetVersion);
            attachment.Version = 1;
            attachment.CreatedUtc = attachment.LastUpdateUtc = DateTime.UtcNow;
            await _Driver.ExecuteInTransactionAsync(async (c, t, ct) =>
            {
                await AdoPromptDocumentMethods.LockTenantAsync(_Driver, c, t, attachment.TenantId, ct).ConfigureAwait(false);
                List<AgentPromptDocument> current = await ListInTransactionAsync(c, t, attachment.TenantId, attachment.AgentProfileId, ct).ConfigureAwait(false);
                if (current.Any(a => a.DocumentId == attachment.DocumentId)) throw new ConflictException("duplicate_attachment", "This document is already attached to the agent.");
                if (current.Count >= MaxAttachmentsPerProfile) throw new PromptDocumentException("document_attachment_limit", "The agent has reached its configured document attachment limit.");
                await ValidateRelationAsync(c, t, attachment, ct).ConfigureAwait(false);
                await ChangeSetAsync(c, t, attachment.TenantId, attachment.AgentProfileId, expectedSetVersion, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(c, t, AdoPromptDocumentMethods.Insert(_Table, PromptDocumentSql.AgentPromptDocumentColumns), command => PromptDocumentSql.BindAgentPromptDocument(command, attachment), ct).ConfigureAwait(false);
                await AdoPromptDocumentMethods.ValidateNamesAsync(_Driver, c, t, attachment.TenantId, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return attachment;
        }

        /// <inheritdoc />
        public async Task<AgentPromptDocument> UpdateAsync(AgentPromptDocument attachment, long expectedVersion, long expectedSetVersion, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            Validate(attachment);
            AdoPromptDocumentMethods.Version(expectedVersion);
            AdoPromptDocumentMethods.Version(expectedSetVersion);
            await _Driver.ExecuteInTransactionAsync(async (c, t, ct) =>
            {
                await AdoPromptDocumentMethods.LockTenantAsync(_Driver, c, t, attachment.TenantId, ct).ConfigureAwait(false);
                List<AgentPromptDocument> current = await ListInTransactionAsync(c, t, attachment.TenantId, attachment.AgentProfileId, ct).ConfigureAwait(false);
                AgentPromptDocument old = current.FirstOrDefault(a => a.Id == attachment.Id) ?? throw new NotFoundException("Attachment not found.");
                if (old.Version != expectedVersion) throw new ConflictException("version_conflict", "Attachment changed concurrently.");
                if (old.DocumentId != attachment.DocumentId) throw new ValidationException("An attachment's document cannot change; detach and add another document.");
                await ValidateRelationAsync(c, t, attachment, ct).ConfigureAwait(false);
                bool changed = old.DeliveryMode != attachment.DeliveryMode || old.RevisionPolicy != attachment.RevisionPolicy || old.PinnedRevision != attachment.PinnedRevision || old.DisplayOrder != attachment.DisplayOrder || old.Enabled != attachment.Enabled || old.AllowPublicAccess != attachment.AllowPublicAccess;
                await CheckSetAsync(c, t, attachment.TenantId, attachment.AgentProfileId, expectedSetVersion, ct).ConfigureAwait(false);
                if (!changed) { attachment.Version = old.Version; attachment.CreatedUtc = old.CreatedUtc; attachment.LastUpdateUtc = old.LastUpdateUtc; return; }
                await ChangeSetAsync(c, t, attachment.TenantId, attachment.AgentProfileId, expectedSetVersion, ct).ConfigureAwait(false);
                attachment.Version = AdoPromptDocumentMethods.Next(expectedVersion);
                attachment.CreatedUtc = old.CreatedUtc;
                attachment.LastUpdateUtc = DateTime.UtcNow;
                string sets = String.Join(", ", PromptDocumentSql.AgentPromptDocumentColumns.Split(", ").Where(x => x != "id" && x != "tenant_id" && x != "agent_profile_id" && x != "document_id" && x != "created_utc").Select(x => x + "=@" + x));
                int affected = await AdoDatabaseDriver.ExecuteAsync(c, t, "UPDATE " + _Table + " SET " + sets + " WHERE tenant_id=@tenant_id AND agent_profile_id=@agent_profile_id AND id=@id AND version=@expected;", command => { PromptDocumentSql.BindAgentPromptDocument(command, attachment); AdoDatabaseDriver.Add(command, "expected", expectedVersion); }, ct).ConfigureAwait(false);
                if (affected != 1) throw new ConflictException("version_conflict", "Attachment changed concurrently.");
                await AdoPromptDocumentMethods.ValidateNamesAsync(_Driver, c, t, attachment.TenantId, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return attachment;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string profileId, string id, long expectedVersion, long expectedSetVersion, CancellationToken token = default)
        {
            AdoPromptDocumentMethods.Scope(tenantId, profileId);
            AdoPromptDocumentMethods.Version(expectedVersion);
            AdoPromptDocumentMethods.Version(expectedSetVersion);
            bool deleted = false;
            await _Driver.ExecuteInTransactionAsync(async (c, t, ct) =>
            {
                await AdoPromptDocumentMethods.LockTenantAsync(_Driver, c, t, tenantId, ct).ConfigureAwait(false);
                List<AgentPromptDocument> current = await ListInTransactionAsync(c, t, tenantId, profileId, ct).ConfigureAwait(false);
                AgentPromptDocument? old = current.FirstOrDefault(a => a.Id == id);
                if (old == null) return;
                if (old.Version != expectedVersion) throw new ConflictException("version_conflict", "Attachment changed concurrently.");
                await ChangeSetAsync(c, t, tenantId, profileId, expectedSetVersion, ct).ConfigureAwait(false);
                deleted = await AdoDatabaseDriver.ExecuteAsync(c, t, "DELETE FROM " + _Table + " WHERE tenant_id=@tenant AND agent_profile_id=@profile AND id=@id AND version=@expected;", command => { AdoPromptDocumentMethods.BindScope(command, tenantId, id); AdoDatabaseDriver.Add(command, "profile", profileId); AdoDatabaseDriver.Add(command, "expected", expectedVersion); }, ct).ConfigureAwait(false) == 1;
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<long> ReorderAsync(string tenantId, string profileId, List<string> attachmentIds, long expectedSetVersion, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(attachmentIds);
            AdoPromptDocumentMethods.Scope(tenantId, profileId);
            AdoPromptDocumentMethods.Version(expectedSetVersion);
            long result = expectedSetVersion;
            await _Driver.ExecuteInTransactionAsync(async (c, t, ct) =>
            {
                await AdoPromptDocumentMethods.LockTenantAsync(_Driver, c, t, tenantId, ct).ConfigureAwait(false);
                List<AgentPromptDocument> current = await ListInTransactionAsync(c, t, tenantId, profileId, ct).ConfigureAwait(false);
                if (attachmentIds.Count != current.Count || attachmentIds.Distinct(StringComparer.Ordinal).Count() != attachmentIds.Count || !current.All(a => attachmentIds.Contains(a.Id, StringComparer.Ordinal))) throw new ConflictException("attachment_set_changed", "Reorder requires every current attachment exactly once.");
                await CheckSetAsync(c, t, tenantId, profileId, expectedSetVersion, ct).ConfigureAwait(false);
                if (current.Select(a => a.Id).SequenceEqual(attachmentIds)) return;
                await ChangeSetAsync(c, t, tenantId, profileId, expectedSetVersion, ct).ConfigureAwait(false);
                for (int i = 0; i < attachmentIds.Count; i++)
                {
                    AgentPromptDocument row = current.Single(a => a.Id == attachmentIds[i]);
                    AdoPromptDocumentMethods.Next(row.Version);
                    int order = i;
                    await AdoDatabaseDriver.ExecuteAsync(c, t, "UPDATE " + _Table + " SET display_order=@ordering, version=version+1, last_update_utc=@updated WHERE tenant_id=@tenant AND agent_profile_id=@profile AND id=@id;", command => { AdoPromptDocumentMethods.BindScope(command, tenantId, row.Id); AdoDatabaseDriver.Add(command, "profile", profileId); AdoDatabaseDriver.Add(command, "ordering", order); AdoDatabaseDriver.Add(command, "updated", DateTime.UtcNow); }, ct).ConfigureAwait(false);
                }
                result = AdoPromptDocumentMethods.Next(expectedSetVersion);
            }, token).ConfigureAwait(false);
            return result;
        }

        private Task<List<AgentPromptDocument>> ListInTransactionAsync(DbConnection c, DbTransaction t, string tenant, string profile, CancellationToken token)
        {
            return AdoDatabaseDriver.QueryAsync(c, t, "SELECT * FROM " + _Table + " WHERE tenant_id=@tenant AND agent_profile_id=@id ORDER BY display_order, id;", command => AdoPromptDocumentMethods.BindScope(command, tenant, profile), PromptDocumentSql.MapAgentPromptDocument, token);
        }

        private async Task ValidateRelationAsync(DbConnection c, DbTransaction t, AgentPromptDocument a, CancellationToken token)
        {
            List<PromptDocument> documents = await AdoDatabaseDriver.QueryAsync(c, t, "SELECT * FROM " + _Driver.Table("prompt_documents") + " WHERE tenant_id=@tenant AND id=@id;", command => AdoPromptDocumentMethods.BindScope(command, a.TenantId, a.DocumentId), PromptDocumentSql.MapPromptDocument, token).ConfigureAwait(false);
            PromptDocument d = documents.FirstOrDefault() ?? throw new NotFoundException("Document not found.");
            int selected = a.RevisionPolicy == PromptDocumentRevisionPolicyEnum.Pinned ? a.PinnedRevision!.Value : d.CurrentRevision;
            List<int> revisions = await AdoDatabaseDriver.QueryAsync(c, t, "SELECT revision FROM " + _Driver.Table("prompt_document_revisions") + " WHERE tenant_id=@tenant AND document_id=@id AND revision=@revision;", command => { AdoPromptDocumentMethods.BindScope(command, a.TenantId, a.DocumentId); AdoDatabaseDriver.Add(command, "revision", selected); }, r => AdoConverters.Int(r, "revision"), token).ConfigureAwait(false);
            if (!revisions.Any()) throw new ConflictException("invalid_pin", "The selected document revision is unavailable.");
        }

        private async Task CheckSetAsync(DbConnection c, DbTransaction t, string tenant, string profile, long version, CancellationToken token)
        {
            List<long> versions = await AdoDatabaseDriver.QueryAsync(c, t, "SELECT document_set_version FROM " + _Driver.Table("agent_profiles") + " WHERE tenant_id=@tenant AND id=@id;", command => AdoPromptDocumentMethods.BindScope(command, tenant, profile), r => AdoConverters.Long(r, "document_set_version"), token).ConfigureAwait(false);
            if (!versions.Any()) throw new NotFoundException("Agent profile not found.");
            if (versions[0] != version) throw new ConflictException("attachment_set_changed", "The agent's document attachments changed. Reload before saving.");
        }

        private async Task ChangeSetAsync(DbConnection c, DbTransaction t, string tenant, string profile, long version, CancellationToken token)
        {
            AdoPromptDocumentMethods.Next(version);
            int changed = await AdoDatabaseDriver.ExecuteAsync(c, t, "UPDATE " + _Driver.Table("agent_profiles") + " SET document_set_version=document_set_version+1, chat_revision=chat_revision+1, last_update_utc=@updated WHERE tenant_id=@tenant AND id=@id AND document_set_version=@expected;", command => { AdoPromptDocumentMethods.BindScope(command, tenant, profile); AdoDatabaseDriver.Add(command, "expected", version); AdoDatabaseDriver.Add(command, "updated", DateTime.UtcNow); }, token).ConfigureAwait(false);
            if (changed != 1) throw new ConflictException("attachment_set_changed", "The agent's document attachments changed. Reload before saving.");
        }

        private static void Validate(AgentPromptDocument a)
        {
            AdoPromptDocumentMethods.Scope(a.TenantId, a.AgentProfileId);
            AdoPromptDocumentMethods.Scope(a.TenantId, a.DocumentId);
            if (!Enum.IsDefined(a.DeliveryMode) || !Enum.IsDefined(a.RevisionPolicy)) throw new ValidationException("Document delivery mode or revision policy is invalid.");
            if ((a.RevisionPolicy == PromptDocumentRevisionPolicyEnum.Latest && a.PinnedRevision != null) || (a.RevisionPolicy == PromptDocumentRevisionPolicyEnum.Pinned && (a.PinnedRevision == null || a.PinnedRevision < 1))) throw new ValidationException("Pinned requires a valid revision; Latest requires no pin.");
            if (a.DisplayOrder < 0) throw new ValidationException("Display order must be nonnegative.");
        }
    }
}
