namespace Hugo.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A connector descriptor with its settings schema, secret slots, and tools.
    /// </summary>
    public class Connector
    {
        #region Public-Members

        /// <summary>
        /// Type.
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Family.
        /// </summary>
        public string? Family { get; set; } = null;

        /// <summary>
        /// Display name.
        /// </summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Category.
        /// </summary>
        public string? Category { get; set; } = null;

        /// <summary>
        /// Icon key.
        /// </summary>
        public string? IconKey { get; set; } = null;

        /// <summary>
        /// Version.
        /// </summary>
        public string? Version { get; set; } = null;

        /// <summary>
        /// Documentation URL.
        /// </summary>
        public string? DocumentationUrl { get; set; } = null;

        /// <summary>
        /// Default instructions key.
        /// </summary>
        public string? DefaultInstructionsKey { get; set; } = null;

        /// <summary>
        /// Plugin.
        /// </summary>
        public bool Plugin { get; set; } = false;

        /// <summary>
        /// Settings.
        /// </summary>
        public List<ConnectorSetting> Settings { get; set; } = new List<ConnectorSetting>();

        /// <summary>
        /// Secret slots.
        /// </summary>
        public List<ConnectorSecretSlot> SecretSlots { get; set; } = new List<ConnectorSecretSlot>();

        /// <summary>
        /// Tools.
        /// </summary>
        public List<ConnectorTool> Tools { get; set; } = new List<ConnectorTool>();

        #endregion
    }
}
