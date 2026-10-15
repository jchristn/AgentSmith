namespace Hugo.Sdk.Requests
{
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Create, update, or test a resource (preview). Null properties are omitted.
    /// </summary>
    public class ResourceRequest
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Connector type.
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Display name.
        /// </summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Endpoint.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Environment.
        /// </summary>
        public string? Environment { get; set; } = null;

        /// <summary>
        /// Notes.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Private or Tenant.
        /// </summary>
        public string? Visibility { get; set; } = null;

        /// <summary>
        /// ReadOnly, ReadWriteWithApproval, or ReadWrite.
        /// </summary>
        public string? DefaultAccessModeCeiling { get; set; } = null;

        /// <summary>
        /// Clear access mode ceiling.
        /// </summary>
        public bool? ClearAccessModeCeiling { get; set; } = null;

        /// <summary>
        /// Active.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Settings by key; values are strings, numbers, booleans, or string lists.
        /// </summary>
        public Dictionary<string, object?>? Settings { get; set; } = null;

        /// <summary>
        /// Secret bindings.
        /// </summary>
        public List<ResourceSecretBinding>? SecretBindings { get; set; } = null;

        #endregion
    }
}
