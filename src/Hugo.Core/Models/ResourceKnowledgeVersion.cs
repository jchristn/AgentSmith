namespace Hugo.Core.Models
{
    using System;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Helpers;

    /// <summary>
    /// One historical version of a resource knowledge entry (resource_knowledge_versions). Not thread-safe.
    /// </summary>
    public class ResourceKnowledgeVersion
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix rkv_).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewResourceKnowledgeVersionId() : value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                _TenantId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Knowledge entry identifier.
        /// </summary>
        public string KnowledgeId
        {
            get
            {
                return _KnowledgeId;
            }
            set
            {
                _KnowledgeId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Resource identifier.
        /// </summary>
        public string ResourceId
        {
            get
            {
                return _ResourceId;
            }
            set
            {
                _ResourceId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Scope.
        /// </summary>
        public string Scope
        {
            get
            {
                return _Scope;
            }
            set
            {
                _Scope = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Key; _root for the scope root (see <see cref="ResourceKnowledge.NormalizeKey"/>).
        /// </summary>
        public string Key
        {
            get
            {
                return _Key;
            }
            set
            {
                _Key = ResourceKnowledge.NormalizeKey(value);
            }
        }

        /// <summary>
        /// Version number.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Content at this version.
        /// </summary>
        public string Content
        {
            get
            {
                return _Content;
            }
            set
            {
                _Content = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Source at this version.
        /// </summary>
        public KnowledgeSourceEnum Source { get; set; } = KnowledgeSourceEnum.Operator;

        /// <summary>
        /// Who wrote this version.
        /// </summary>
        public string? UpdatedBy { get; set; } = null;

        /// <summary>
        /// Whether this version records a deletion.
        /// </summary>
        public bool Deleted { get; set; } = false;

        /// <summary>
        /// When this version was written (UTC).
        /// </summary>
        public DateTime CreatedUtc
        {
            get
            {
                return _CreatedUtc;
            }
            set
            {
                _CreatedUtc = TimestampHelper.Normalize(value);
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewResourceKnowledgeVersionId();
        private string _TenantId = String.Empty;
        private string _KnowledgeId = String.Empty;
        private string _ResourceId = String.Empty;
        private string _Scope = String.Empty;
        private string _Key = Constants.KnowledgeRootKey;
        private string _Content = String.Empty;
        private DateTime _CreatedUtc = DateTime.UtcNow;

        #endregion
    }
}
