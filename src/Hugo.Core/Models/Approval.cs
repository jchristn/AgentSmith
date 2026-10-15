namespace Hugo.Core.Models
{
    using System;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// A pending or decided human approval for one tool call (approvals, plan sections 7.5 and 11.4). Not thread-safe.
    /// </summary>
    public class Approval
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix apr_).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewApprovalId() : value;
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
        /// Tool invocation identifier.
        /// </summary>
        public string ToolInvocationId
        {
            get
            {
                return _ToolInvocationId;
            }
            set
            {
                _ToolInvocationId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Profile identifier.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Binding identifier.
        /// </summary>
        public string? BindingId { get; set; } = null;

        /// <summary>
        /// Binding alias.
        /// </summary>
        public string? BindingAlias { get; set; } = null;

        /// <summary>
        /// Resource identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Tool name.
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
        /// Surface the call came from.
        /// </summary>
        public InvocationSurfaceEnum Surface { get; set; } = InvocationSurfaceEnum.Rest;

        /// <summary>
        /// Requester principal type.
        /// </summary>
        public PrincipalTypeEnum RequestedByPrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// Requester principal identifier.
        /// </summary>
        public string? RequestedByPrincipalId { get; set; } = null;

        /// <summary>
        /// Requester display name.
        /// </summary>
        public string? RequestedByName { get; set; } = null;

        /// <summary>
        /// Human-readable action (for example Run sudo systemctl restart nginx on web-01).
        /// </summary>
        public string Summary
        {
            get
            {
                return _Summary;
            }
            set
            {
                _Summary = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Classified effect.
        /// </summary>
        public EffectEnum Effect { get; set; } = EffectEnum.Write;

        /// <summary>
        /// Classifier or policy reason.
        /// </summary>
        public string? EffectReason { get; set; } = null;

        /// <summary>
        /// Redacted arguments JSON, for the approval card.
        /// </summary>
        public string? ArgumentsRedacted { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public ApprovalStatusEnum Status { get; set; } = ApprovalStatusEnum.Pending;

        /// <summary>
        /// Who decided (user or principal identifier).
        /// </summary>
        public string? DecidedByUserId { get; set; } = null;

        /// <summary>
        /// Display name of who decided.
        /// </summary>
        public string? DecidedByName { get; set; } = null;

        /// <summary>
        /// Note from the approver.
        /// </summary>
        public string? DecisionNote { get; set; } = null;

        /// <summary>
        /// When it was decided (UTC).
        /// </summary>
        public DateTime? DecidedUtc { get; set; } = null;

        /// <summary>
        /// When the approval expires (UTC).
        /// </summary>
        public DateTime ExpiresUtc
        {
            get
            {
                return _ExpiresUtc;
            }
            set
            {
                _ExpiresUtc = TimestampHelper.Normalize(value);
            }
        }

        /// <summary>
        /// The tool invocation, with the result once executed. Populated on single reads; not persisted.
        /// </summary>
        public ToolInvocation? Invocation { get; set; } = null;

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

        #region Private-Members

        private string _Id = IdGenerator.NewApprovalId();
        private string _TenantId = String.Empty;
        private string _ToolInvocationId = String.Empty;
        private string _ToolName = String.Empty;
        private string _Summary = String.Empty;
        private DateTime _ExpiresUtc = DateTime.UtcNow;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
