namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Data.Common;
    using System.Globalization;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;

    internal static class PromptDocumentSql
    {
        internal static readonly string PromptDocumentColumns = "id, tenant_id, owner_user_id, visibility, name, normalized_name, description, active, current_revision, metadata_version, created_utc, last_update_utc";
        internal static PromptDocument MapPromptDocument(DbDataReader r)
        {
            return new PromptDocument
            {
                Id = AdoConverters.Str(r, "id"),
                TenantId = AdoConverters.Str(r, "tenant_id"),
                OwnerUserId = AdoConverters.NullableStr(r, "owner_user_id"),
                Visibility = AdoConverters.Enum(r, "visibility", VisibilityEnum.Private),
                Name = AdoConverters.Str(r, "name"),
                NormalizedName = AdoConverters.Str(r, "normalized_name"),
                Description = AdoConverters.Str(r, "description"),
                Active = AdoConverters.Bool(r, "active"),
                CurrentRevision = AdoConverters.Int(r, "current_revision"),
                Version = AdoConverters.Long(r, "metadata_version"),
                CreatedUtc = Convert.ToDateTime(r["created_utc"], CultureInfo.InvariantCulture).ToUniversalTime(),
                LastUpdateUtc = Convert.ToDateTime(r["last_update_utc"], CultureInfo.InvariantCulture).ToUniversalTime(),
            };
        }
        internal static void BindPromptDocument(DbCommand c, PromptDocument o)
        {
            AdoDatabaseDriver.Add(c, "id", o.Id);
            AdoDatabaseDriver.Add(c, "tenant_id", o.TenantId);
            AdoDatabaseDriver.Add(c, "owner_user_id", o.OwnerUserId);
            AdoDatabaseDriver.Add(c, "visibility", o.Visibility.ToString());
            AdoDatabaseDriver.Add(c, "name", o.Name);
            AdoDatabaseDriver.Add(c, "normalized_name", o.NormalizedName);
            AdoDatabaseDriver.Add(c, "description", o.Description);
            AdoDatabaseDriver.Add(c, "active", o.Active);
            AdoDatabaseDriver.Add(c, "current_revision", o.CurrentRevision);
            AdoDatabaseDriver.Add(c, "metadata_version", o.Version);
            AdoDatabaseDriver.Add(c, "created_utc", o.CreatedUtc);
            AdoDatabaseDriver.Add(c, "last_update_utc", o.LastUpdateUtc);
        }
        internal static readonly string PromptDocumentRevisionColumns = "id, tenant_id, document_id, revision, name, description, markdown, content_bytes, content_hash, created_by_user_id, created_utc";
        internal static PromptDocumentRevision MapPromptDocumentRevision(DbDataReader r)
        {
            return new PromptDocumentRevision
            {
                Id = AdoConverters.Str(r, "id"),
                TenantId = AdoConverters.Str(r, "tenant_id"),
                DocumentId = AdoConverters.Str(r, "document_id"),
                Revision = AdoConverters.Int(r, "revision"),
                Name = AdoConverters.Str(r, "name"),
                Description = AdoConverters.Str(r, "description"),
                Markdown = AdoConverters.Str(r, "markdown"),
                ContentBytes = AdoConverters.Int(r, "content_bytes"),
                ContentHash = AdoConverters.Str(r, "content_hash"),
                CreatedByUserId = AdoConverters.NullableStr(r, "created_by_user_id"),
                CreatedUtc = Convert.ToDateTime(r["created_utc"], CultureInfo.InvariantCulture).ToUniversalTime(),
            };
        }
        internal static PromptDocumentRevisionInfo MapPromptDocumentRevisionInfo(DbDataReader r)
        {
            return new PromptDocumentRevisionInfo
            {
                Id = AdoConverters.Str(r, "id"),
                DocumentId = AdoConverters.Str(r, "document_id"),
                Revision = AdoConverters.Int(r, "revision"),
                Name = AdoConverters.Str(r, "name"),
                Description = AdoConverters.Str(r, "description"),
                ContentBytes = AdoConverters.Int(r, "content_bytes"),
                ContentHash = AdoConverters.Str(r, "content_hash"),
                CreatedByUserId = AdoConverters.NullableStr(r, "created_by_user_id"),
                CreatedUtc = Convert.ToDateTime(r["created_utc"], CultureInfo.InvariantCulture).ToUniversalTime(),
            };
        }
        internal static void BindPromptDocumentRevision(DbCommand c, PromptDocumentRevision o)
        {
            AdoDatabaseDriver.Add(c, "id", o.Id);
            AdoDatabaseDriver.Add(c, "tenant_id", o.TenantId);
            AdoDatabaseDriver.Add(c, "document_id", o.DocumentId);
            AdoDatabaseDriver.Add(c, "revision", o.Revision);
            AdoDatabaseDriver.Add(c, "name", o.Name);
            AdoDatabaseDriver.Add(c, "description", o.Description);
            AdoDatabaseDriver.Add(c, "markdown", o.Markdown);
            AdoDatabaseDriver.Add(c, "content_bytes", o.ContentBytes);
            AdoDatabaseDriver.Add(c, "content_hash", o.ContentHash);
            AdoDatabaseDriver.Add(c, "created_by_user_id", o.CreatedByUserId);
            AdoDatabaseDriver.Add(c, "created_utc", o.CreatedUtc);
        }
        internal static readonly string AgentPromptDocumentColumns = "id, tenant_id, agent_profile_id, document_id, delivery_mode, revision_policy, pinned_revision, display_order, enabled, allow_public_access, version, created_utc, last_update_utc";
        internal static AgentPromptDocument MapAgentPromptDocument(DbDataReader r)
        {
            return new AgentPromptDocument
            {
                Id = AdoConverters.Str(r, "id"),
                TenantId = AdoConverters.Str(r, "tenant_id"),
                AgentProfileId = AdoConverters.Str(r, "agent_profile_id"),
                DocumentId = AdoConverters.Str(r, "document_id"),
                DeliveryMode = AdoConverters.Enum(r, "delivery_mode", PromptDocumentDeliveryModeEnum.Reference),
                RevisionPolicy = AdoConverters.Enum(r, "revision_policy", PromptDocumentRevisionPolicyEnum.Latest),
                PinnedRevision = AdoConverters.NullableInt(r, "pinned_revision"),
                DisplayOrder = AdoConverters.Int(r, "display_order"),
                Enabled = AdoConverters.Bool(r, "enabled"),
                AllowPublicAccess = AdoConverters.Bool(r, "allow_public_access"),
                Version = AdoConverters.Long(r, "version"),
                CreatedUtc = Convert.ToDateTime(r["created_utc"], CultureInfo.InvariantCulture).ToUniversalTime(),
                LastUpdateUtc = Convert.ToDateTime(r["last_update_utc"], CultureInfo.InvariantCulture).ToUniversalTime(),
            };
        }
        internal static void BindAgentPromptDocument(DbCommand c, AgentPromptDocument o)
        {
            AdoDatabaseDriver.Add(c, "id", o.Id);
            AdoDatabaseDriver.Add(c, "tenant_id", o.TenantId);
            AdoDatabaseDriver.Add(c, "agent_profile_id", o.AgentProfileId);
            AdoDatabaseDriver.Add(c, "document_id", o.DocumentId);
            AdoDatabaseDriver.Add(c, "delivery_mode", o.DeliveryMode.ToString());
            AdoDatabaseDriver.Add(c, "revision_policy", o.RevisionPolicy.ToString());
            AdoDatabaseDriver.Add(c, "pinned_revision", o.PinnedRevision);
            AdoDatabaseDriver.Add(c, "display_order", o.DisplayOrder);
            AdoDatabaseDriver.Add(c, "enabled", o.Enabled);
            AdoDatabaseDriver.Add(c, "allow_public_access", o.AllowPublicAccess);
            AdoDatabaseDriver.Add(c, "version", o.Version);
            AdoDatabaseDriver.Add(c, "created_utc", o.CreatedUtc);
            AdoDatabaseDriver.Add(c, "last_update_utc", o.LastUpdateUtc);
        }
    }
}
