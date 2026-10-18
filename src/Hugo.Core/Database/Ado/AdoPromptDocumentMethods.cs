namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enums;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Responses;
    using Hugo.Core.Models;
    using Hugo.Core.Services;

    /// <summary>Transactional tenant-isolated versioned document persistence. Thread-safe.</summary>
    public class AdoPromptDocumentMethods : IPromptDocumentMethods
    {
        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private readonly string _Revisions;
        private int _MaxDocumentBytes = 262144;
        private int _MaxDocumentsPerTenant = 256;
        private int _MaxStorageBytesPerTenant = 67108864;

        /// <inheritdoc />
        public int MaxDocumentsPerTenant { get { return Volatile.Read(ref _MaxDocumentsPerTenant); } set { if (value < 1 || value > 10000) throw new ArgumentOutOfRangeException(nameof(value)); Volatile.Write(ref _MaxDocumentsPerTenant, value); } }
        /// <inheritdoc />
        public int MaxStorageBytesPerTenant { get { return Volatile.Read(ref _MaxStorageBytesPerTenant); } set { if (value < 1048576 || value > 1073741824) throw new ArgumentOutOfRangeException(nameof(value)); Volatile.Write(ref _MaxStorageBytesPerTenant, value); } }


        /// <inheritdoc />
        public int MaxDocumentBytes { get { return Volatile.Read(ref _MaxDocumentBytes); } set { if (value < 1024 || value > 1048576) throw new ArgumentOutOfRangeException(nameof(value)); Volatile.Write(ref _MaxDocumentBytes, value); } }

        /// <summary>Initialize document methods.</summary>
        /// <param name="driver">Database driver.</param>
        public AdoPromptDocumentMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("prompt_documents");
            _Revisions = driver.Table("prompt_document_revisions");
        }

        /// <inheritdoc />
        public async Task<PromptDocument> CreateAsync(PromptDocument document, PromptDocumentRevision revision, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(revision);
            Prepare(document, revision);
            document.Version = 1;
            document.CurrentRevision = 1;
            document.CreatedUtc = document.LastUpdateUtc = DateTime.UtcNow;
            revision.Revision = 1;
            await _Driver.ExecuteInTransactionAsync(async (c, t, ct) =>
            {
                await LockTenantAsync(_Driver, c, t, document.TenantId, ct).ConfigureAwait(false);
                await CheckQuotaAsync(c, t, document.TenantId, revision.ContentBytes, true, ct).ConfigureAwait(false);
                await CheckNameAsync(c, t, document, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(c, t, Insert(_Table, PromptDocumentSql.PromptDocumentColumns), command => PromptDocumentSql.BindPromptDocument(command, document), ct).ConfigureAwait(false);
                await InsertRevisionAsync(c, t, revision, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return document;
        }

        /// <inheritdoc />
        public Task<PromptDocument?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            Scope(tenantId, id);
            return _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE tenant_id=@tenant AND id=@id;", command => BindScope(command, tenantId, id), PromptDocumentSql.MapPromptDocument, token);
        }

        /// <inheritdoc />
        public Task<List<PromptDocument>> ListAsync(string tenantId, CancellationToken token = default)
        {
            Scope(tenantId, tenantId);
            return _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE tenant_id=@tenant ORDER BY normalized_name, id;", command => AdoDatabaseDriver.Add(command, "tenant", tenantId), PromptDocumentSql.MapPromptDocument, token);
        }

        /// <inheritdoc />
        public Task<PromptDocumentRevision?> ReadRevisionAsync(string tenantId, string documentId, int revision, CancellationToken token = default)
        {
            Scope(tenantId, documentId);
            return _Driver.QuerySingleAsync("SELECT * FROM " + _Revisions + " WHERE tenant_id=@tenant AND document_id=@id AND revision=@revision;", command => { BindScope(command, tenantId, documentId); AdoDatabaseDriver.Add(command, "revision", revision); }, PromptDocumentSql.MapPromptDocumentRevision, token);
        }

        /// <inheritdoc />
        public Task<PromptDocumentRevisionInfo?> ReadRevisionMetadataAsync(string tenantId, string documentId, int revision, CancellationToken token = default)
        {
            Scope(tenantId, documentId);
            string columns = PromptDocumentSql.PromptDocumentRevisionColumns.Replace("markdown, ", String.Empty);
            return _Driver.QuerySingleAsync("SELECT " + columns + " FROM " + _Revisions + " WHERE tenant_id=@tenant AND document_id=@id AND revision=@revision;", command => { BindScope(command, tenantId, documentId); AdoDatabaseDriver.Add(command, "revision", revision); }, PromptDocumentSql.MapPromptDocumentRevisionInfo, token);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<PromptDocumentRevisionInfo>> ListRevisionMetadataAsync(string tenantId, string documentId, EnumerationQuery query, CancellationToken token = default)
        {
            Scope(tenantId, documentId);
            ArgumentNullException.ThrowIfNull(query);
            string columns = PromptDocumentSql.PromptDocumentRevisionColumns.Replace("markdown, ", String.Empty);
            object? count = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Revisions + " WHERE tenant_id=@tenant AND document_id=@id;", command => BindScope(command, tenantId, documentId), token).ConfigureAwait(false);
            List<PromptDocumentRevisionInfo> page = await _Driver.QueryAsync("SELECT " + columns + " FROM " + _Revisions + " WHERE tenant_id=@tenant AND document_id=@id ORDER BY revision DESC" + _Driver.Dialect.LimitOffsetClause() + ";", command => { BindScope(command, tenantId, documentId); AdoDatabaseDriver.Add(command, "max", query.MaxResults); AdoDatabaseDriver.Add(command, "skip", query.Skip); }, PromptDocumentSql.MapPromptDocumentRevisionInfo, token).ConfigureAwait(false);
            return EnumerationResult<PromptDocumentRevisionInfo>.Build(query, Convert.ToInt64(count), page);
        }

        /// <inheritdoc />
        public Task<List<PromptDocumentRevision>> ListRevisionsAsync(string tenantId, string documentId, CancellationToken token = default)
        {
            Scope(tenantId, documentId);
            return _Driver.QueryAsync("SELECT * FROM " + _Revisions + " WHERE tenant_id=@tenant AND document_id=@id ORDER BY revision DESC;", command => BindScope(command, tenantId, documentId), PromptDocumentSql.MapPromptDocumentRevision, token);
        }

        /// <inheritdoc />
        public Task<bool> TryUpdateAsync(PromptDocument document, PromptDocumentRevision? revision, long expectedVersion, CancellationToken token = default)
        {
            return TryUpdateAsync(document, revision, expectedVersion, false, token);
        }

        /// <inheritdoc />
        public async Task<bool> TryUpdateAsync(PromptDocument document, PromptDocumentRevision? revision, long expectedVersion, bool authorizedPublicRevisionChange, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            Prepare(document, revision);
            Version(expectedVersion);
            bool success = false;
            await _Driver.ExecuteInTransactionAsync(async (c, t, ct) =>
            {
                await LockTenantAsync(_Driver, c, t, document.TenantId, ct).ConfigureAwait(false);
                List<PromptDocument> rows = await AdoDatabaseDriver.QueryAsync(c, t, "SELECT * FROM " + _Table + " WHERE tenant_id=@tenant AND id=@id;", command => BindScope(command, document.TenantId, document.Id), PromptDocumentSql.MapPromptDocument, ct).ConfigureAwait(false);
                PromptDocument? old = rows.FirstOrDefault();
                if (old == null || old.Version != expectedVersion) return;
                List<PromptDocumentRevision> revisions = await AdoDatabaseDriver.QueryAsync(c, t, "SELECT * FROM " + _Revisions + " WHERE tenant_id=@tenant AND document_id=@id AND revision=@revision;", command => { BindScope(command, document.TenantId, document.Id); AdoDatabaseDriver.Add(command, "revision", old.CurrentRevision); }, PromptDocumentSql.MapPromptDocumentRevision, ct).ConfigureAwait(false);
                PromptDocumentRevision current = revisions.Single();
                bool newRevision = old.Name != document.Name || old.Description != document.Description || (revision != null && current.Markdown != revision.Markdown);
                bool metadataChange = old.Visibility != document.Visibility || old.Active != document.Active || old.OwnerUserId != document.OwnerUserId;
                if (!newRevision && !metadataChange) { document.Version = old.Version; document.CurrentRevision = old.CurrentRevision; document.LastUpdateUtc = old.LastUpdateUtc; success = true; return; }
                await CheckNameAsync(c, t, document, ct).ConfigureAwait(false);
                document.CurrentRevision = newRevision ? checked(old.CurrentRevision + 1) : old.CurrentRevision;
                document.Version = Next(expectedVersion);
                document.CreatedUtc = old.CreatedUtc;
                document.LastUpdateUtc = DateTime.UtcNow;
                if (newRevision)
                {
                    if (!authorizedPublicRevisionChange)
                    {
                        List<string> publicLatest = await AdoDatabaseDriver.QueryAsync(c, t, "SELECT id FROM " + _Driver.Table("agent_prompt_documents") + " WHERE tenant_id=@tenant AND document_id=@id AND allow_public_access=" + _Driver.Dialect.BooleanLiteral(true) + " AND revision_policy='Latest';", command => BindScope(command, document.TenantId, document.Id), r => AdoConverters.Str(r, "id"), ct).ConfigureAwait(false);
                        if (publicLatest.Any()) throw new ForbiddenException("document_admin_required", "Changing a document used by public Latest attachments requires document Admin permission.");
                    }
                    revision ??= new PromptDocumentRevision { Markdown = current.Markdown, CreatedByUserId = document.OwnerUserId };
                    Prepare(document, revision);
                    await CheckQuotaAsync(c, t, document.TenantId, revision.ContentBytes, false, ct).ConfigureAwait(false);
                    revision.Revision = document.CurrentRevision;
                    await InsertRevisionAsync(c, t, revision, ct).ConfigureAwait(false);
                }
                string sets = String.Join(", ", PromptDocumentSql.PromptDocumentColumns.Split(", ").Where(x => x != "id" && x != "tenant_id" && x != "created_utc").Select(x => x + "=@" + x));
                int changed = await AdoDatabaseDriver.ExecuteAsync(c, t, "UPDATE " + _Table + " SET " + sets + " WHERE tenant_id=@tenant_id AND id=@id AND metadata_version=@expected;", command => { PromptDocumentSql.BindPromptDocument(command, document); AdoDatabaseDriver.Add(command, "expected", expectedVersion); }, ct).ConfigureAwait(false);
                if (changed != 1) throw new ConflictException("version_conflict", "Document changed concurrently.");
                await ValidateNamesAsync(_Driver, c, t, document.TenantId, ct).ConfigureAwait(false);
                if (old.Active != document.Active || old.Visibility != document.Visibility || old.OwnerUserId != document.OwnerUserId)
                    await RevokeReferrersAsync(c, t, document.TenantId, document.Id, ct).ConfigureAwait(false);
                success = true;
            }, token).ConfigureAwait(false);
            return success;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, long expectedVersion, CancellationToken token = default)
        {
            Scope(tenantId, id);
            Version(expectedVersion);
            bool deleted = false;
            await _Driver.ExecuteInTransactionAsync(async (c, t, ct) =>
            {
                await LockTenantAsync(_Driver, c, t, tenantId, ct).ConfigureAwait(false);
                List<PromptDocument> documents = await AdoDatabaseDriver.QueryAsync(c, t, "SELECT * FROM " + _Table + " WHERE tenant_id=@tenant AND id=@id;", command => BindScope(command, tenantId, id), PromptDocumentSql.MapPromptDocument, ct).ConfigureAwait(false);
                if (!documents.Any()) return;
                if (documents[0].Version != expectedVersion) throw new ConflictException("version_conflict", "Document changed concurrently.");
                List<string> refs = await AdoDatabaseDriver.QueryAsync(c, t, "SELECT id FROM " + _Driver.Table("agent_prompt_documents") + " WHERE tenant_id=@tenant AND document_id=@id;", command => BindScope(command, tenantId, id), r => AdoConverters.Str(r, "id"), ct).ConfigureAwait(false);
                if (refs.Any()) throw new ConflictException("document_in_use", "Detach this document from its agents before deleting it.");
                await AdoDatabaseDriver.ExecuteAsync(c, t, "DELETE FROM " + _Revisions + " WHERE tenant_id=@tenant AND document_id=@id;", command => BindScope(command, tenantId, id), ct).ConfigureAwait(false);
                deleted = await AdoDatabaseDriver.ExecuteAsync(c, t, "DELETE FROM " + _Table + " WHERE tenant_id=@tenant AND id=@id;", command => BindScope(command, tenantId, id), ct).ConfigureAwait(false) == 1;
            }, token).ConfigureAwait(false);
            return deleted;
        }

        internal static string Insert(string table, string columns) { return "INSERT INTO " + table + " (" + columns + ") VALUES (@" + columns.Replace(", ", ", @") + ");"; }
        internal static void Scope(string tenant, string id) { if (String.IsNullOrWhiteSpace(tenant) || String.IsNullOrWhiteSpace(id)) throw new ArgumentException("Tenant and identifier are required."); }
        internal static void BindScope(DbCommand c, string tenant, string id) { AdoDatabaseDriver.Add(c, "tenant", tenant); AdoDatabaseDriver.Add(c, "id", id); }
        internal static void Version(long version) { if (version < 0 || version > 9007199254740991L) throw new ArgumentOutOfRangeException(nameof(version), "Version is outside the supported safe integer range."); }
        internal static long Next(long version) { Version(version); if (version == 9007199254740991L) throw new ConflictException("version_exhausted", "The version counter is exhausted; this record cannot be changed safely."); return version + 1; }

        internal static async Task LockTenantAsync(AdoDatabaseDriver driver, DbConnection c, DbTransaction t, string tenant, CancellationToken token)
        {
            Scope(tenant, tenant);
            int count = await AdoDatabaseDriver.ExecuteAsync(c, t, "UPDATE " + driver.Table("tenants") + " SET last_update_utc=last_update_utc WHERE id=@tenant;", command => AdoDatabaseDriver.Add(command, "tenant", tenant), token).ConfigureAwait(false);
            if (count != 1) throw new NotFoundException("Tenant not found.");
        }

        internal static async Task ValidateNamesAsync(AdoDatabaseDriver driver, DbConnection c, DbTransaction t, string tenant, CancellationToken token)
        {
            string sql = "SELECT a.agent_profile_id, r.name FROM " + driver.Table("agent_prompt_documents") + " a JOIN " + driver.Table("prompt_documents") + " d ON d.tenant_id=a.tenant_id AND d.id=a.document_id JOIN " + driver.Table("prompt_document_revisions") + " r ON r.tenant_id=d.tenant_id AND r.document_id=d.id AND r.revision=CASE WHEN a.revision_policy='Pinned' THEN a.pinned_revision ELSE d.current_revision END WHERE a.tenant_id=@tenant;";
            List<string> names = await AdoDatabaseDriver.QueryAsync(c, t, sql, command => AdoDatabaseDriver.Add(command, "tenant", tenant), r => AdoConverters.Str(r, "agent_profile_id") + "\0" + AdoConverters.Str(r, "name").Normalize(NormalizationForm.FormC).ToUpperInvariant(), token).ConfigureAwait(false);
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Count) throw new ConflictException("document_name_conflict", "An agent already has an attachment with the selected filename.");
        }

        private async Task CheckQuotaAsync(DbConnection c, DbTransaction t, string tenant, int additionalBytes, bool creating, CancellationToken token)
        {
            if (creating)
            {
                List<long> count = await AdoDatabaseDriver.QueryAsync(c, t, "SELECT COUNT(*) FROM " + _Table + " WHERE tenant_id=@tenant;", command => AdoDatabaseDriver.Add(command, "tenant", tenant), r => Convert.ToInt64(r.GetValue(0)), token).ConfigureAwait(false);
                if (count[0] >= MaxDocumentsPerTenant) throw new PromptDocumentException("document_count_limit", "The tenant has reached its document count limit. Delete unused documents or increase the quota.");
            }
            List<long> sizes = await AdoDatabaseDriver.QueryAsync(c, t, "SELECT COALESCE(SUM(content_bytes),0) FROM " + _Revisions + " WHERE tenant_id=@tenant;", command => AdoDatabaseDriver.Add(command, "tenant", tenant), r => Convert.ToInt64(r.GetValue(0)), token).ConfigureAwait(false);
            if (sizes[0] + additionalBytes > MaxStorageBytesPerTenant) throw new PromptDocumentException("document_storage_limit", "The tenant has reached its retained document storage limit. Delete unused documents or increase the quota.");
        }

        private async Task CheckNameAsync(DbConnection c, DbTransaction t, PromptDocument d, CancellationToken token)
        {
            List<string> same = await AdoDatabaseDriver.QueryAsync(c, t, "SELECT id FROM " + _Table + " WHERE tenant_id=@tenant AND normalized_name=@name AND id<>@id;", command => { BindScope(command, d.TenantId, d.Id); AdoDatabaseDriver.Add(command, "name", d.NormalizedName); }, r => AdoConverters.Str(r, "id"), token).ConfigureAwait(false);
            if (same.Any()) throw new ConflictException("name_conflict", "A document with this filename already exists.");
        }

        private Task<int> InsertRevisionAsync(DbConnection c, DbTransaction t, PromptDocumentRevision revision, CancellationToken token) { return AdoDatabaseDriver.ExecuteAsync(c, t, Insert(_Revisions, PromptDocumentSql.PromptDocumentRevisionColumns), command => PromptDocumentSql.BindPromptDocumentRevision(command, revision), token); }

        private Task<int> RevokeReferrersAsync(DbConnection c, DbTransaction t, string tenant, string document, CancellationToken token)
        {
            return AdoDatabaseDriver.ExecuteAsync(c, t, "UPDATE " + _Driver.Table("agent_profiles") + " SET chat_revision=chat_revision+1 WHERE tenant_id=@tenant AND id IN (SELECT agent_profile_id FROM " + _Driver.Table("agent_prompt_documents") + " WHERE tenant_id=@tenant AND document_id=@id);", command => BindScope(command, tenant, document), token);
        }

        private void Prepare(PromptDocument d, PromptDocumentRevision? r)
        {
            Scope(d.TenantId, d.Id);
            string name = (d.Name ?? String.Empty).Normalize(NormalizationForm.FormC);
            if (name.Length < 1 || name.Length > 128 || name.Any(Char.IsControl) || name.Contains('/') || name.Contains('\\') || name == "." || name == ".." || (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))) throw new ValidationException("Provide a Markdown or text filename without path separators or control characters.");
            if (String.IsNullOrWhiteSpace(d.Description) || d.Description.Length > 1024) throw new ValidationException("Document description must contain 1 through 1024 characters.");
            if (!Enum.IsDefined(d.Visibility)) throw new ValidationException("Document visibility is invalid.");
            d.Name = name;
            d.NormalizedName = name.ToUpperInvariant();
            if (r == null) return;
            r.TenantId = d.TenantId;
            r.DocumentId = d.Id;
            r.Name = d.Name;
            r.Description = d.Description;
            string markdown = r.Markdown ?? String.Empty;
            if (markdown.StartsWith('\uFEFF')) markdown = markdown.Substring(1);
            markdown = markdown.Replace("\r\n", "\n").Replace("\r", "\n");
            if (markdown.IndexOf('\0') >= 0) throw new ValidationException("Document contains binary or NUL data.");
            byte[] bytes;
            try { bytes = new UTF8Encoding(false, true).GetBytes(markdown); }
            catch (EncoderFallbackException) { throw new ValidationException("Document must contain valid UTF-8 text."); }
            if (bytes.Length > MaxDocumentBytes) throw new PromptDocumentException("document_size_limit", "Document exceeds the configured maximum UTF-8 byte size.");
            r.Markdown = markdown;
            r.ContentBytes = bytes.Length;
            r.ContentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            r.CreatedUtc = DateTime.UtcNow;
        }
    }
}
