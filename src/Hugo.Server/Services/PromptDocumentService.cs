namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;
    using Hugo.Core.Services;

    /// <summary>Authorized management of immutable document revisions and versioned profile attachments. Thread-safe.</summary>
    public class PromptDocumentService
    {
        private readonly ServerContext _Server;
        private readonly AuthorizationService _Authorization;

        /// <summary>Instantiate with uncached authorization so revocation applies to every delivery.</summary>
        /// <param name="server">Server dependencies.</param>
        public PromptDocumentService(ServerContext server)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Authorization = new AuthorizationService(server.Database, server.Audit) { CacheSeconds = 0 };
        }

        /// <summary>Fresh permission evaluation shared by management, composition and retrieval.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="type">Resource.</param><param name="operation">Operation.</param><param name="id">Identifier.</param><param name="token">Cancellation.</param>
        /// <returns>Whether access is allowed.</returns>
        public async Task<bool> AllowedAsync(RequestContext ctx, string tenant, ResourceTypeEnum type, OperationTypeEnum operation, string? id, CancellationToken token)
        {
            if (!ctx.IsAuthenticated || (!ctx.IsAdmin && ctx.PrincipalType != PrincipalTypeEnum.Administrator && ctx.TenantId != tenant)) return false;
            return (await _Authorization.EvaluateAsync(ctx, tenant, type, operation, id, token).ConfigureAwait(false)).Permitted;
        }

        /// <summary>Read a visible document with a specific permission.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="id">Document.</param><param name="operation">Permission.</param><param name="token">Cancellation.</param>
        /// <returns>Authorized metadata.</returns>
        public async Task<PromptDocument> DocumentAsync(RequestContext ctx, string tenant, string id, OperationTypeEnum operation, CancellationToken token)
        {
            TenantCheck(ctx, tenant);
            PromptDocument? document = await _Server.Database.PromptDocuments.ReadAsync(tenant, id, token).ConfigureAwait(false);
            if (document == null || !ResourceVisibility.CanSee(document.Visibility, document.OwnerUserId, ctx)) throw new NotFoundException("Prompt document not found.");
            await DemandAsync(ctx, tenant, ResourceTypeEnum.PromptDocument, operation, id, token).ConfigureAwait(false);
            return document;
        }

        /// <summary>Read a visible profile with a specific permission.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="id">Profile.</param><param name="operation">Permission.</param><param name="token">Cancellation.</param>
        /// <returns>Profile.</returns>
        public async Task<AgentProfile> ProfileAsync(RequestContext ctx, string tenant, string id, OperationTypeEnum operation, CancellationToken token)
        {
            TenantCheck(ctx, tenant);
            AgentProfile? profile = await _Server.Database.AgentProfiles.ReadAsync(tenant, id, token).ConfigureAwait(false);
            if (profile == null || (ctx.Credential?.LockToProfile == true && ctx.Credential.AgentProfileId != profile.Id) || !ResourceVisibility.CanSee(profile, ctx)) throw new NotFoundException("Agent profile not found.");
            if (ctx.Credential?.LockToProfile == true && ctx.Credential.AgentProfileId != id) throw new NotFoundException("Agent profile not found.");
            await DemandAsync(ctx, tenant, ResourceTypeEnum.AgentProfile, operation, id, token).ConfigureAwait(false);
            return profile;
        }

        /// <summary>List visible authorized metadata without revision bodies.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="query">Paging.</param><param name="active">Optional state.</param><param name="token">Cancellation.</param>
        /// <param name="visibility">Optional visibility filter.</param>
        /// <returns>Metadata page.</returns>
        public async Task<EnumerationResult<PromptDocumentView>> ListAsync(RequestContext ctx, string tenant, EnumerationQuery query, bool? active, CancellationToken token, VisibilityEnum? visibility = null)
        {
            TenantCheck(ctx, tenant);
            List<PromptDocument> visible = new List<PromptDocument>();
            foreach (PromptDocument document in await _Server.Database.PromptDocuments.ListAsync(tenant, token).ConfigureAwait(false))
            {
                if (visibility.HasValue && document.Visibility != visibility.Value) continue;
                if (active.HasValue && document.Active != active.Value) continue;
                if (!String.IsNullOrEmpty(query.Search) && !document.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase)) continue;
                if (!ResourceVisibility.CanSee(document.Visibility, document.OwnerUserId, ctx) || !await AllowedAsync(ctx, tenant, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Read, document.Id, token).ConfigureAwait(false)) continue;
                visible.Add(document);
            }
            IEnumerable<PromptDocument> ordered = query.Ordering.ToString().Contains("Ascending", StringComparison.Ordinal) ? visible.OrderBy(d => d.CreatedUtc) : visible.OrderByDescending(d => d.CreatedUtc);
            if (query.Ordering.ToString().Contains("Name", StringComparison.Ordinal)) ordered = query.Ordering.ToString().Contains("Descending", StringComparison.Ordinal) ? visible.OrderByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase) : visible.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);
            List<PromptDocumentView> page = new List<PromptDocumentView>();
            foreach (PromptDocument document in ordered.Skip(query.Skip).Take(query.MaxResults)) page.Add(await ViewAsync(ctx, document, token).ConfigureAwait(false));
            return EnumerationResult<PromptDocumentView>.Build(query, visible.Count, page);
        }

        /// <summary>Read metadata and caller capabilities without Markdown.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="id">Document.</param><param name="token">Cancellation.</param>
        /// <returns>Metadata.</returns>
        public async Task<PromptDocumentView> ReadAsync(RequestContext ctx, string tenant, string id, CancellationToken token)
        {
            return await ViewAsync(ctx, await DocumentAsync(ctx, tenant, id, OperationTypeEnum.Read, token).ConfigureAwait(false), token).ConfigureAwait(false);
        }

        /// <summary>Create managed metadata and revision one atomically.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="request">Content.</param><param name="token">Cancellation.</param>
        /// <returns>Created metadata.</returns>
        public async Task<PromptDocumentView> CreateAsync(RequestContext ctx, string tenant, PromptDocumentRequest request, CancellationToken token)
        {
            TenantCheck(ctx, tenant); await DemandAsync(ctx, tenant, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Create, null, token).ConfigureAwait(false);
            ConfigureLimits();
            ValidateContent(request.Name, request.Description, request.Markdown);
            PromptDocument document = new PromptDocument { TenantId = tenant, OwnerUserId = ctx.UserId, Name = request.Name ?? String.Empty, Description = request.Description ?? String.Empty, Visibility = request.Visibility ?? VisibilityEnum.Private, Active = request.Active ?? true };
            PromptDocumentRevision revision = new PromptDocumentRevision { TenantId = tenant, DocumentId = document.Id, Name = document.Name, Description = document.Description, Markdown = request.Markdown ?? String.Empty, CreatedByUserId = ctx.UserId };
            await _Server.Database.PromptDocuments.CreateAsync(document, revision, token).ConfigureAwait(false);
            await ChangedAsync(ctx, tenant, document.Id, "created", token).ConfigureAwait(false);
            return await ViewAsync(ctx, (await _Server.Database.PromptDocuments.ReadAsync(tenant, document.Id, token).ConfigureAwait(false))!, token).ConfigureAwait(false);
        }

        /// <summary>CAS metadata and optional immutable revision; public Latest changes also require Admin.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="id">Document.</param><param name="request">Patch.</param><param name="token">Cancellation.</param>
        /// <returns>Current metadata.</returns>
        public async Task<PromptDocumentView> UpdateAsync(RequestContext ctx, string tenant, string id, PromptDocumentRequest request, CancellationToken token)
        {
            long expected = Version(request.ExpectedVersion, "expectedVersion", 1);
            PromptDocument document = await DocumentAsync(ctx, tenant, id, OperationTypeEnum.Update, token).ConfigureAwait(false);
            PromptDocumentRevision current = await RevisionCoreAsync(document, document.CurrentRevision, token).ConfigureAwait(false);
            string name = request.Name ?? document.Name, description = request.Description ?? document.Description, markdown = request.Markdown ?? current.Markdown;
            bool newRevision = name != current.Name || description != current.Description || Canonical(markdown) != current.Markdown;
            bool canPublish = await AllowedAsync(ctx, tenant, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Admin, id, token).ConfigureAwait(false);
            if (newRevision && (await _Server.Database.AgentPromptDocuments.ReferrersAsync(tenant, id, token).ConfigureAwait(false)).Any(a => a.AllowPublicAccess && a.RevisionPolicy == PromptDocumentRevisionPolicyEnum.Latest))
                await DemandAsync(ctx, tenant, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Admin, id, token).ConfigureAwait(false);
            if (request.Name != null || request.Description != null || request.Markdown != null) ValidateContent(name, description, markdown);
            document.Name = name; document.Description = description; document.Visibility = request.Visibility ?? document.Visibility; document.Active = request.Active ?? document.Active;
            ConfigureLimits();
            PromptDocumentRevision? revision = newRevision ? new PromptDocumentRevision { TenantId = tenant, DocumentId = id, Name = name, Description = description, Markdown = markdown, CreatedByUserId = ctx.UserId } : null;
            if (!await _Server.Database.PromptDocuments.TryUpdateAsync(document, revision, expected, canPublish, token).ConfigureAwait(false)) throw new ConflictException("version_conflict", "The document changed; reload its current version before saving.");
            await ChangedAsync(ctx, tenant, id, "updated", token).ConfigureAwait(false);
            return await ViewAsync(ctx, (await _Server.Database.PromptDocuments.ReadAsync(tenant, id, token).ConfigureAwait(false))!, token).ConfigureAwait(false);
        }

        /// <summary>Delete only a detached document using its metadata version.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="id">Document.</param><param name="expected">Version.</param><param name="token">Cancellation.</param>
        public async Task DeleteAsync(RequestContext ctx, string tenant, string id, long? expected, CancellationToken token)
        {
            await DocumentAsync(ctx, tenant, id, OperationTypeEnum.Delete, token).ConfigureAwait(false);
            await _Server.Database.PromptDocuments.DeleteAsync(tenant, id, Version(expected, "expectedVersion", 1), token).ConfigureAwait(false);
            await ChangedAsync(ctx, tenant, id, "deleted", token).ConfigureAwait(false);
        }

        /// <summary>List immutable revision metadata only.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="id">Document.</param><param name="query">Paging.</param><param name="token">Cancellation.</param>
        /// <returns>Revision metadata.</returns>
        public async Task<EnumerationResult<PromptDocumentRevisionInfo>> RevisionsAsync(RequestContext ctx, string tenant, string id, EnumerationQuery query, CancellationToken token)
        {
            await DocumentAsync(ctx, tenant, id, OperationTypeEnum.Read, token).ConfigureAwait(false);
            return await _Server.Database.PromptDocuments.ListRevisionMetadataAsync(tenant, id, query, token).ConfigureAwait(false);
        }

        /// <summary>Explicit authorized immutable body read for managers.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="id">Document.</param><param name="revision">Revision.</param><param name="token">Cancellation.</param>
        /// <returns>Full canonical revision.</returns>
        public async Task<PromptDocumentRevision> RevisionAsync(RequestContext ctx, string tenant, string id, int revision, CancellationToken token)
        {
            PromptDocument document = await DocumentAsync(ctx, tenant, id, OperationTypeEnum.Read, token).ConfigureAwait(false);
            ctx.SuppressHistoryBodies = true;
            return await RevisionCoreAsync(document, revision, token).ConfigureAwait(false);
        }

        /// <summary>List authorized referrers without hidden names or counts.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="id">Document.</param><param name="token">Cancellation.</param>
        /// <returns>Visible referrers and a hidden-referrer flag.</returns>
        public async Task<PromptDocumentReferrers> ReferrersAsync(RequestContext ctx, string tenant, string id, CancellationToken token)
        {
            await DocumentAsync(ctx, tenant, id, OperationTypeEnum.Read, token).ConfigureAwait(false);
            PromptDocumentReferrers result = new PromptDocumentReferrers();
            foreach (AgentPromptDocument row in await _Server.Database.AgentPromptDocuments.ReferrersAsync(tenant, id, token).ConfigureAwait(false))
            {
                AgentProfile? profile = await _Server.Database.AgentProfiles.ReadAsync(tenant, row.AgentProfileId, token).ConfigureAwait(false);
                if (profile == null || (ctx.Credential?.LockToProfile == true && ctx.Credential.AgentProfileId != profile.Id) || !ResourceVisibility.CanSee(profile, ctx) || !await AllowedAsync(ctx, tenant, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, profile.Id, token).ConfigureAwait(false)) { result.HasOtherReferrers = true; continue; }
                result.Items.Add(new PromptDocumentReferrer { ProfileId = profile.Id, ProfileName = profile.Name, AttachmentId = row.Id, DeliveryMode = row.DeliveryMode, RevisionPolicy = row.RevisionPolicy, PinnedRevision = row.PinnedRevision, Enabled = row.Enabled, AllowPublicAccess = row.AllowPublicAccess });
            }
            return result;
        }

        /// <summary>List an authorized manager's attachments with selected revision metadata.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="profileId">Profile.</param><param name="token">Cancellation.</param>
        /// <returns>Rows and set version.</returns>
        public async Task<AgentPromptDocumentList> AttachmentsAsync(RequestContext ctx, string tenant, string profileId, CancellationToken token)
        {
            AgentProfile profile = await ProfileAsync(ctx, tenant, profileId, OperationTypeEnum.Update, token).ConfigureAwait(false);
            AgentPromptDocumentList result = new AgentPromptDocumentList { DocumentSetVersion = profile.DocumentSetVersion };
            foreach (AgentPromptDocument attachment in await _Server.Database.AgentPromptDocuments.ListAsync(tenant, profileId, token).ConfigureAwait(false))
            {
                PromptDocument document = await DocumentAsync(ctx, tenant, attachment.DocumentId, OperationTypeEnum.Read, token).ConfigureAwait(false);
                result.Items.Add(await AttachmentViewAsync(attachment, document, profile.DocumentSetVersion, token).ConfigureAwait(false));
            }
            return result;
        }

        /// <summary>Add or update a versioned attachment, enforcing document publication approval.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="profileId">Profile.</param><param name="attachmentId">Existing attachment, or null for create.</param><param name="request">Patch.</param><param name="token">Cancellation.</param>
        /// <returns>Resolved attachment with the new set version.</returns>
        public async Task<AgentPromptDocumentView> SaveAttachmentAsync(RequestContext ctx, string tenant, string profileId, string? attachmentId, AgentPromptDocumentRequest request, CancellationToken token)
        {
            AgentProfile profile = await ProfileAsync(ctx, tenant, profileId, OperationTypeEnum.Update, token).ConfigureAwait(false);
            long setVersion = Version(request.ExpectedDocumentSetVersion, "expectedDocumentSetVersion", 0);
            AgentPromptDocument? previous = attachmentId == null ? null : await _Server.Database.AgentPromptDocuments.ReadAsync(tenant, profileId, attachmentId, token).ConfigureAwait(false) ?? throw new NotFoundException("Attachment not found.");
            if (previous != null && request.DocumentId != null && request.DocumentId != previous.DocumentId) throw Invalid("documentId", "Document identity cannot change; detach and add instead.");
            AgentPromptDocument row = previous == null ? new AgentPromptDocument { TenantId = tenant, AgentProfileId = profileId, DocumentId = request.DocumentId ?? String.Empty } : Serializer.Copy(previous)!;
            PromptDocument document = await DocumentAsync(ctx, tenant, row.DocumentId, OperationTypeEnum.Read, token).ConfigureAwait(false);
            row.DeliveryMode = request.DeliveryMode ?? row.DeliveryMode; row.RevisionPolicy = request.RevisionPolicy ?? row.RevisionPolicy;
            row.PinnedRevision = row.RevisionPolicy == PromptDocumentRevisionPolicyEnum.Latest ? null : request.PinnedRevision ?? row.PinnedRevision;
            row.DisplayOrder = request.DisplayOrder ?? row.DisplayOrder; row.Enabled = request.Enabled ?? row.Enabled; row.AllowPublicAccess = request.AllowPublicAccess ?? row.AllowPublicAccess;
            if (!Enum.IsDefined(row.DeliveryMode) || !Enum.IsDefined(row.RevisionPolicy) || (row.RevisionPolicy == PromptDocumentRevisionPolicyEnum.Pinned && (!row.PinnedRevision.HasValue || row.PinnedRevision <= 0))) throw Invalid("pinnedRevision", "Pinned delivery requires a valid immutable revision.");
            if (row.AllowPublicAccess && (previous == null || !previous.AllowPublicAccess || previous.RevisionPolicy != row.RevisionPolicy || previous.PinnedRevision != row.PinnedRevision || (!previous.Enabled && row.Enabled)))
                await DemandAsync(ctx, tenant, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Admin, document.Id, token).ConfigureAwait(false);
            await RevisionMetadataAsync(document, row.PinnedRevision ?? document.CurrentRevision, token).ConfigureAwait(false);
            if (profile.ChatEnabled && profile.ChatAccessMode == ChatAccessModeEnum.Public && row.AllowPublicAccess && row.Enabled) await ValidatePublicDocumentAsync(profile, document, token).ConfigureAwait(false);
            ConfigureLimits();
            AgentPromptDocument stored = previous == null ? await _Server.Database.AgentPromptDocuments.CreateAsync(row, setVersion, token).ConfigureAwait(false) : await _Server.Database.AgentPromptDocuments.UpdateAsync(row, Version(request.ExpectedVersion, "expectedVersion", 1), setVersion, token).ConfigureAwait(false);
            await ChangedAsync(ctx, tenant, profileId, "attachment_saved", token).ConfigureAwait(false);
            AgentProfile refreshed = (await _Server.Database.AgentProfiles.ReadAsync(tenant, profileId, token).ConfigureAwait(false))!;
            return await AttachmentViewAsync(stored, document, refreshed.DocumentSetVersion, token).ConfigureAwait(false);
        }

        /// <summary>Detach without deleting the library document.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="profileId">Profile.</param><param name="id">Attachment.</param><param name="version">Row version.</param><param name="set">Set version.</param><param name="token">Cancellation.</param>
        public async Task DetachAsync(RequestContext ctx, string tenant, string profileId, string id, long? version, long? set, CancellationToken token)
        {
            await ProfileAsync(ctx, tenant, profileId, OperationTypeEnum.Update, token).ConfigureAwait(false);
            AgentPromptDocument row = await _Server.Database.AgentPromptDocuments.ReadAsync(tenant, profileId, id, token).ConfigureAwait(false) ?? throw new NotFoundException("Attachment not found.");
            await DocumentAsync(ctx, tenant, row.DocumentId, OperationTypeEnum.Read, token).ConfigureAwait(false);
            await _Server.Database.AgentPromptDocuments.DeleteAsync(tenant, profileId, id, Version(version, "expectedVersion", 1), Version(set, "expectedDocumentSetVersion", 0), token).ConfigureAwait(false);
            await ChangedAsync(ctx, tenant, profileId, "attachment_detached", token).ConfigureAwait(false);
        }

        /// <summary>CAS exact attachment membership and order.</summary>
        /// <param name="ctx">Caller.</param><param name="tenant">Tenant.</param><param name="profileId">Profile.</param><param name="request">Ordered IDs and set version.</param><param name="token">Cancellation.</param>
        /// <returns>New set version.</returns>
        public async Task<long> ReorderAsync(RequestContext ctx, string tenant, string profileId, AgentPromptDocumentOrderRequest request, CancellationToken token)
        {
            await AttachmentsAsync(ctx, tenant, profileId, token).ConfigureAwait(false);
            long version = await _Server.Database.AgentPromptDocuments.ReorderAsync(tenant, profileId, request.AttachmentIds, Version(request.ExpectedDocumentSetVersion, "expectedDocumentSetVersion", 0), token).ConfigureAwait(false);
            await ChangedAsync(ctx, tenant, profileId, "attachment_reordered", token).ConfigureAwait(false); return version;
        }

        /// <summary>Read a selected revision for a previously authorized attachment.</summary>
        /// <param name="row">Attachment.</param><param name="document">Document.</param><param name="setVersion">Set version.</param><param name="token">Cancellation.</param>
        /// <returns>Metadata only.</returns>
        public async Task<AgentPromptDocumentView> AttachmentViewAsync(AgentPromptDocument row, PromptDocument document, long setVersion, CancellationToken token)
        {
            PromptDocumentRevisionInfo revision = await RevisionMetadataAsync(document, row.RevisionPolicy == PromptDocumentRevisionPolicyEnum.Pinned ? row.PinnedRevision ?? 0 : document.CurrentRevision, token).ConfigureAwait(false);
            AgentPromptDocumentView view = Serializer.Deserialize<AgentPromptDocumentView>(Serializer.Serialize(row))!;
            view.Name = revision.Name; view.Description = revision.Description; view.Revision = revision.Revision; view.ResolvedRevision = revision.Revision;
            view.ContentBytes = revision.ContentBytes; view.ContentHash = revision.ContentHash; view.DocumentSetVersion = setVersion; return view;
        }

        /// <summary>Validate all opted-in documents before enabling a saved public publication.</summary>
        /// <param name="ctx">Publishing manager.</param><param name="profile">Proposed profile settings.</param><param name="token">Cancellation.</param>
        public async Task ValidatePublicationAsync(RequestContext ctx, AgentProfile profile, CancellationToken token)
        {
            foreach (AgentPromptDocument row in await _Server.Database.AgentPromptDocuments.ListAsync(profile.TenantId, profile.Id, token).ConfigureAwait(false))
            {
                if (!row.Enabled || !row.AllowPublicAccess) continue;
                PromptDocument? document = await _Server.Database.PromptDocuments.ReadAsync(profile.TenantId, row.DocumentId, token).ConfigureAwait(false);
                if (document == null || !document.Active) continue;
                await ValidatePublicDocumentAsync(profile, document, token).ConfigureAwait(false);
            }
        }

        /// <summary>Verify selected credential's explicit document grant within its owner's ceiling without modifying grants.</summary>
        /// <param name="profile">Publication.</param><param name="document">Document.</param><param name="token">Cancellation.</param>
        public async Task ValidatePublicDocumentAsync(AgentProfile profile, PromptDocument document, CancellationToken token)
        {
            RequestContext credential = await PublishingPrincipalAsync(profile, token).ConfigureAwait(false);
            List<PermissionGrant> grants = await _Authorization.GrantsForCredentialAsync(profile.TenantId, credential.CredentialId!, token).ConfigureAwait(false);
            bool explicitRead = grants.Any(g => g.PermissionType == PermissionTypeEnum.Permit && g.Scope == ResourceScopeEnum.Resource && g.ResourceId == document.Id && (g.ResourceTypes.Contains("PromptDocument") || g.ResourceTypes.Contains("All")) && (g.Operations.Contains("Read") || g.Operations.Contains("All")));
            if (!explicitRead || !ResourceVisibility.CanSee(document.Visibility, document.OwnerUserId, credential) || !await AllowedAsync(credential, profile.TenantId, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Read, document.Id, token).ConfigureAwait(false))
                throw Invalid("allowPublicAccess", "The selected publication credential needs an explicit resource-scoped PromptDocument Read grant for every opted-in document, within its owner's permissions. Configure grants in Access; this operation never changes them.");
        }

        /// <summary>Build an uncapped preview identity from the saved publishing credential; normal grants still apply.</summary>
        /// <param name="profile">Publication profile.</param><param name="token">Cancellation.</param>
        /// <returns>Credential identity with owner bypass disabled.</returns>
        public async Task<RequestContext> PublishingPrincipalAsync(AgentProfile profile, CancellationToken token)
        {
            Credential? stored = String.IsNullOrEmpty(profile.ChatCredentialId) ? null : await _Server.Database.Credentials.ReadAsync(profile.TenantId, profile.ChatCredentialId, token).ConfigureAwait(false);
            if (stored == null || !stored.Active || stored.ExpiresUtc <= DateTime.UtcNow || stored.AuthMode == CredentialAuthModeEnum.SessionExchange) throw Invalid("audience", "A valid publishing credential must be selected before public preview or delivery.");
            User? owner = await _Server.Database.Users.ReadAsync(profile.TenantId, stored.UserId, token).ConfigureAwait(false);
            if (owner == null || !owner.Active || (stored.LockToProfile && stored.AgentProfileId != profile.Id)) throw Invalid("audience", "The publishing identity is inactive or locked to another profile.");
            Credential copy = Serializer.Copy(stored)!; copy.InheritOwnerBypass = false; copy.LockToProfile = true; copy.AgentProfileId = profile.Id;
            return new RequestContext { IsAuthenticated = true, AuthResult = AuthResultEnum.Success, PrincipalType = PrincipalTypeEnum.Credential, TenantId = profile.TenantId, UserId = owner.Id, CredentialId = copy.Id, Credential = copy, User = owner };
        }

        private async Task<PromptDocumentView> ViewAsync(RequestContext ctx, PromptDocument document, CancellationToken token)
        {
            PromptDocumentView result = Serializer.Deserialize<PromptDocumentView>(Serializer.Serialize(document))!;
            PromptDocumentRevisionInfo revision = await RevisionMetadataAsync(document, document.CurrentRevision, token).ConfigureAwait(false);
            result.ContentBytes = revision.ContentBytes; result.ContentHash = revision.ContentHash; result.CanRead = await AllowedAsync(ctx, document.TenantId, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Read, document.Id, token).ConfigureAwait(false);
            result.CanUpdate = await AllowedAsync(ctx, document.TenantId, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Update, document.Id, token).ConfigureAwait(false);
            result.CanDelete = await AllowedAsync(ctx, document.TenantId, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Delete, document.Id, token).ConfigureAwait(false);
            result.CanAdmin = await AllowedAsync(ctx, document.TenantId, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Admin, document.Id, token).ConfigureAwait(false);
            result.HasReferrers = (await _Server.Database.AgentPromptDocuments.ReferrersAsync(document.TenantId, document.Id, token).ConfigureAwait(false)).Count > 0;
            return result;
        }

        private async Task<PromptDocumentRevisionInfo> RevisionMetadataAsync(PromptDocument document, int revision, CancellationToken token)
        {
            return await _Server.Database.PromptDocuments.ReadRevisionMetadataAsync(document.TenantId, document.Id, revision, token).ConfigureAwait(false) ?? throw new NotFoundException("Document revision not found.");
        }

        private async Task<PromptDocumentRevision> RevisionCoreAsync(PromptDocument document, int revision, CancellationToken token)
        {
            return await _Server.Database.PromptDocuments.ReadRevisionAsync(document.TenantId, document.Id, revision, token).ConfigureAwait(false) ?? throw new NotFoundException("Document revision not found.");
        }

        private async Task DemandAsync(RequestContext ctx, string tenant, ResourceTypeEnum type, OperationTypeEnum operation, string? id, CancellationToken token)
        {
            if (!(await _Authorization.AuthorizeAsync(ctx, tenant, type, operation, id, token).ConfigureAwait(false)).Permitted) throw new ForbiddenException("permission_denied", "The requested document operation is not permitted.");
        }

        private void ConfigureLimits()
        {
            _Server.Database.PromptDocuments.MaxDocumentBytes = _Server.Settings.PromptDocuments.MaxPromptDocumentBytes;
            _Server.Database.PromptDocuments.MaxDocumentsPerTenant = _Server.Settings.PromptDocuments.MaxPromptDocumentsPerTenant;
            _Server.Database.PromptDocuments.MaxStorageBytesPerTenant = _Server.Settings.PromptDocuments.MaxPromptDocumentStorageBytesPerTenant;
            _Server.Database.AgentPromptDocuments.MaxAttachmentsPerProfile = _Server.Settings.PromptDocuments.MaxPromptDocumentAttachmentsPerProfile;
        }

        private static string Canonical(string text)
        {
            if (text.StartsWith("\uFEFF", StringComparison.Ordinal)) text = text.Substring(1);
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private void ValidateContent(string? name, string? description, string? markdown)
        {
            if (name == null || description == null || markdown == null) throw Invalid("markdown", "Name, description and Markdown are required.");
            UTF8Encoding encoding = new UTF8Encoding(false, true);
            try { if (encoding.GetByteCount(Canonical(markdown)) > _Server.Settings.PromptDocuments.MaxPromptDocumentBytes) throw new PromptDocumentException("document_limit_exceeded", "Document content exceeds the configured UTF-8 byte limit."); }
            catch (EncoderFallbackException) { throw Invalid("markdown", "Markdown must be valid UTF-8 text."); }
            foreach (string value in new[] { name, description, markdown })
                if (!String.Equals(SecretScrubber.Shared.Scrub(value), value, StringComparison.Ordinal) || Regex.IsMatch(value, @"\b(?:eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}|ghp_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|xox[abprs]-[A-Za-z0-9-]{10,}|sk-[A-Za-z0-9_-]{20,}|AIza[0-9A-Za-z_-]{30,})\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) throw Invalid("markdown", "Document fields contain known secrets or credential patterns. Remove secret material before saving.");
        }

        private async Task ChangedAsync(RequestContext ctx, string tenant, string id, string operation, CancellationToken token)
        {
            Hugo.Core.Telemetry.HugoMetrics.Count(Hugo.Core.Telemetry.HugoMetricCatalog.PromptDocumentOperations, "operation", operation, "outcome", "ok");
            _Server.Get<Hugo.Core.Instructions.InstructionCompiler>().Invalidate(tenant);
            await _Server.Audit.WriteAsync(ctx, AuditEventTypeEnum.ProfileChanged, "PromptDocument", id, "Prompt document operation " + operation + ". Content omitted.", tenant, token).ConfigureAwait(false);
            _Server.TryGet<EventHub>()?.Publish("prompt-document.changed", tenant, new { id, operation });
        }

        private static void TenantCheck(RequestContext ctx, string tenant)
        {
            if (!ctx.IsAuthenticated || ctx.IsPublicChat) throw new ForbiddenException("permission_denied", "Document library management is not available to this caller.");
            if (!ctx.IsAdmin && ctx.PrincipalType != PrincipalTypeEnum.Administrator && ctx.TenantId != tenant) throw new NotFoundException("Prompt document not found.");
        }

        /// <summary>Validate required optimistic versions within the JSON-safe integer range.</summary>
        /// <param name="value">Version.</param><param name="field">Field.</param><param name="minimum">Minimum.</param>
        /// <returns>Validated token.</returns>
        public static long Version(long? value, string field, long minimum)
        {
            if (!value.HasValue || value < minimum || value > 9007199254740991L) throw Invalid(field, "A valid current integer version is required."); return value.Value;
        }

        private static ValidationException Invalid(string field, string message)
        {
            return new ValidationException(message, new Dictionary<string, string> { { field, message } });
        }
    }
}
