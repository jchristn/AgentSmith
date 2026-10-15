namespace Hugo.Core.Models
{
    using System;
    using System.Text.Json.Serialization;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// A per-binding rule for one tool (binding_tool_rules, plan section 11.2). Not thread-safe.
    /// </summary>
    public class BindingToolRule
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix btr_).
        /// </summary>
        [JsonIgnore]
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewBindingToolRuleId() : value;
            }
        }

        /// <summary>
        /// Binding identifier.
        /// </summary>
        [JsonIgnore]
        public string BindingId
        {
            get
            {
                return _BindingId;
            }
            set
            {
                _BindingId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        [JsonIgnore]
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
        /// Tool name (for example ssh_exec).
        /// </summary>
        public string ToolName
        {
            get
            {
                return _ToolName;
            }
            set
            {
                _ToolName = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Rule. Deny always wins; RequireApproval upgrades Allowed to approval; Allow never overrides ReadOnly.
        /// </summary>
        public ToolRuleEnum Rule { get; set; } = ToolRuleEnum.Allow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewBindingToolRuleId();
        private string _BindingId = String.Empty;
        private string _TenantId = String.Empty;
        private string _ToolName = String.Empty;

        #endregion
    }
}
