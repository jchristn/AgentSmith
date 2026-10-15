namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// One typed setting of a resource (preview).
    /// </summary>
    public class ResourceSetting
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Resource Identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// String, Text, Integer, Boolean, StringList, or Enum.
        /// </summary>
        public string? ValueKind { get; set; } = null;

        /// <summary>
        /// Value.
        /// </summary>
        public string? Value { get; set; } = null;

        #endregion
    }
}
