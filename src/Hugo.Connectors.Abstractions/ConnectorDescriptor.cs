namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Static description of a connector. Treat as immutable after construction.
    /// </summary>
    public class ConnectorDescriptor
    {
        #region Public-Members

        /// <summary>
        /// Stable connector type identifier, lowercase with hyphens (for example <c>ssh</c>, <c>azure-blob</c>). Required.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Type
        {
            get
            {
                return _Type;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Type));
                _Type = value;
            }
        }

        /// <summary>
        /// Tool family and tool name prefix (for example <c>sql</c> for all four SQL connectors). Connectors in the same
        /// family that expose a tool with the same name must use the same input schema. Defaults to the type.
        /// </summary>
        public string Family
        {
            get
            {
                return String.IsNullOrEmpty(_Family) ? _Type : _Family;
            }
            set
            {
                _Family = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Display name (for example <c>Amazon S3</c>).
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// One-paragraph description for humans.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Category. Default <see cref="ConnectorCategoryEnum.Other"/>.
        /// </summary>
        public ConnectorCategoryEnum Category { get; set; } = ConnectorCategoryEnum.Other;

        /// <summary>
        /// Icon key the dashboard maps to an icon (for example <c>terminal</c>, <c>database</c>, <c>bucket</c>).
        /// </summary>
        public string IconKey { get; set; } = "plug";

        /// <summary>
        /// Connector implementation version (independent of the Hugo product version).
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// Settings schema. Never null.
        /// </summary>
        public ConnectorSettingsSchema SettingsSchema
        {
            get
            {
                return _SettingsSchema;
            }
            set
            {
                _SettingsSchema = value ?? new ConnectorSettingsSchema();
            }
        }

        /// <summary>
        /// Secret slots. Never null.
        /// </summary>
        public List<SecretSlotDescriptor> SecretSlots
        {
            get
            {
                return _SecretSlots;
            }
            set
            {
                _SecretSlots = value ?? new List<SecretSlotDescriptor>();
            }
        }

        /// <summary>
        /// Prompt template key of the default instructions (for example <c>connector.ssh.instructions</c>). Defaults to
        /// <c>connector.{type}.instructions</c>.
        /// </summary>
        public string DefaultInstructionsKey
        {
            get
            {
                return String.IsNullOrEmpty(_DefaultInstructionsKey) ? "connector." + _Type + ".instructions" : _DefaultInstructionsKey;
            }
            set
            {
                _DefaultInstructionsKey = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Built-in default instructions text (Markdown, written for the calling LLM). Seeds the Global layer.
        /// </summary>
        public string DefaultInstructions { get; set; } = String.Empty;

        /// <summary>
        /// Prompt template key of the compact instructions used when a profile has many resources. Defaults to
        /// <c>connector.{type}.compact</c>.
        /// </summary>
        public string CompactInstructionsKey
        {
            get
            {
                return String.IsNullOrEmpty(_CompactInstructionsKey) ? "connector." + _Type + ".compact" : _CompactInstructionsKey;
            }
            set
            {
                _CompactInstructionsKey = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Built-in compact instructions (a few lines: what to call first, the key limits, the one or two things never to
        /// do). When empty, the server looks for an embedded resource named <c>connector.{type}.compact.md</c> in the
        /// connector's assembly, then falls back to a generated line.
        /// </summary>
        public string CompactInstructions { get; set; } = String.Empty;

        /// <summary>
        /// Optional documentation URL for the upstream service.
        /// </summary>
        public string? DocumentationUrl { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Type = "connector";
        private string _Family = String.Empty;
        private string _DefaultInstructionsKey = String.Empty;
        private string _CompactInstructionsKey = String.Empty;
        private ConnectorSettingsSchema _SettingsSchema = new ConnectorSettingsSchema();
        private List<SecretSlotDescriptor> _SecretSlots = new List<SecretSlotDescriptor>();

        #endregion
    }
}
