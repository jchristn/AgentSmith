namespace Hugo.Core.Models
{
    using System;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// An A2A task (a2a_tasks, plan 7.5 and 14.4): one message sent to a profile over A2A, run as a turn of the
    /// conversation bound to its context. Owned by the creating principal (D-006). Not thread-safe.
    /// </summary>
    public class A2ATask
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix a2t_).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.New(Constants.A2ATaskIdPrefix) : value;
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
        /// Agent profile identifier.
        /// </summary>
        public string AgentProfileId
        {
            get
            {
                return _AgentProfileId;
            }
            set
            {
                _AgentProfileId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Principal type of the creator.
        /// </summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// Principal identifier of the creator (user or credential).
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Conversation the task runs in.
        /// </summary>
        public string ConversationId
        {
            get
            {
                return _ConversationId;
            }
            set
            {
                _ConversationId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// A2A context identifier.
        /// </summary>
        public string ContextId
        {
            get
            {
                return _ContextId;
            }
            set
            {
                _ContextId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Client message identifier of the message that started the task, when it sent one.
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// State.
        /// </summary>
        public A2ATaskStateEnum State { get; set; } = A2ATaskStateEnum.Submitted;

        /// <summary>
        /// Status message for the current state (the answer, the pending approval, or the failure).
        /// </summary>
        public string? StatusMessage { get; set; } = null;

        /// <summary>
        /// Approval the task waits for while InputRequired.
        /// </summary>
        public string? PendingApprovalId { get; set; } = null;

        /// <summary>
        /// Input tokens used by the task's turn.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Output tokens used by the task's turn.
        /// </summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>
        /// Tool calls made by the task's turn.
        /// </summary>
        public int ToolCallCount { get; set; } = 0;

        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Time of the last state change (UTC).
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.New(Constants.A2ATaskIdPrefix);
        private string _TenantId = String.Empty;
        private string _AgentProfileId = String.Empty;
        private string _ConversationId = String.Empty;
        private string _ContextId = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the state is final (Completed, Failed, Canceled, Rejected).
        /// </summary>
        /// <returns>True when terminal.</returns>
        public bool IsTerminal()
        {
            return State == A2ATaskStateEnum.Completed || State == A2ATaskStateEnum.Failed || State == A2ATaskStateEnum.Canceled || State == A2ATaskStateEnum.Rejected;
        }

        #endregion
    }
}
