namespace Hugo.Core.Models
{
    using System;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Helpers;

    /// <summary>
    /// One durable knowledge entry of a resource (resource_knowledge, D-038): for example the database context of a SQL
    /// resource. Content is scrubbed before it is stored. Not thread-safe.
    /// </summary>
    public class ResourceKnowledge
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix rkn_).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewResourceKnowledgeId() : value;
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
        /// Scope defined by the connector (for example database, table, host).
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
        /// Key within the scope; <see cref="Constants.KnowledgeRootKey"/> (_root) for the scope root. Setting null, an empty
        /// string, or _root stores _root.
        /// </summary>
        public string Key
        {
            get
            {
                return _Key;
            }
            set
            {
                _Key = NormalizeKey(value);
            }
        }

        /// <summary>
        /// Content (Markdown or plain text).
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
        /// Who produced the content.
        /// </summary>
        public KnowledgeSourceEnum Source { get; set; } = KnowledgeSourceEnum.Operator;

        /// <summary>
        /// Principal or tool that last wrote it.
        /// </summary>
        public string? UpdatedBy { get; set; } = null;

        /// <summary>
        /// Version, incremented on every write. Starts at 1.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Creation timestamp (UTC).
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

        /// <summary>
        /// Last update timestamp (UTC).
        /// </summary>
        public DateTime LastUpdateUtc
        {
            get
            {
                return _LastUpdateUtc;
            }
            set
            {
                _LastUpdateUtc = TimestampHelper.Normalize(value);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The stored form of a knowledge key: null, empty, and _root all mean the scope root and become _root.
        /// </summary>
        /// <param name="key">Key as supplied, or null.</param>
        /// <returns>Stored key.</returns>
        public static string NormalizeKey(string? key)
        {
            if (String.IsNullOrEmpty(key) || key == Constants.KnowledgeRootKey) return Constants.KnowledgeRootKey;
            return key;
        }

        /// <summary>
        /// Whether a key addresses the scope root (null, empty, or _root).
        /// </summary>
        /// <param name="key">Key, or null.</param>
        /// <returns>True for the scope root.</returns>
        public static bool IsRootKey(string? key)
        {
            return NormalizeKey(key) == Constants.KnowledgeRootKey;
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewResourceKnowledgeId();
        private string _TenantId = String.Empty;
        private string _ResourceId = String.Empty;
        private string _Scope = String.Empty;
        private string _Key = Constants.KnowledgeRootKey;
        private string _Content = String.Empty;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
