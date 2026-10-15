namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// Binding of a connector secret slot to a vault secret.
    /// </summary>
    public class ResourceSecretBinding
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
        /// Slot declared by the connector, for example privateKey.
        /// </summary>
        public string SlotName { get; set; } = String.Empty;

        /// <summary>
        /// Secret Identifier.
        /// </summary>
        public string SecretId { get; set; } = String.Empty;

        /// <summary>
        /// Secret field name.
        /// </summary>
        public string? SecretFieldName { get; set; } = null;

        /// <summary>
        /// Secret name.
        /// </summary>
        public string? SecretName { get; set; } = null;

        #endregion
    }
}
