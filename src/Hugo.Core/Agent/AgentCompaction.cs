namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A compaction to persist: a summary message that replaces the covered messages in future history loads.
    /// The covered messages stay in storage (for export and audit) but are marked compacted.
    /// </summary>
    public class AgentCompaction
    {
        #region Public-Members

        /// <summary>
        /// The summary message (role <see cref="AgentMessageRoleEnum.Summary"/>). Never null.
        /// </summary>
        public AgentMessage Summary
        {
            get
            {
                return _Summary;
            }
            set
            {
                _Summary = value ?? new AgentMessage { Role = AgentMessageRoleEnum.Summary };
            }
        }

        /// <summary>
        /// IDs of the messages the summary replaces (oldest first). Never null.
        /// </summary>
        public List<string> CoveredMessageIds
        {
            get
            {
                return _CoveredMessageIds;
            }
            set
            {
                _CoveredMessageIds = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Estimated history tokens before compaction.
        /// </summary>
        public long TokensBefore { get; set; } = 0;

        /// <summary>
        /// Estimated history tokens after compaction.
        /// </summary>
        public long TokensAfter { get; set; } = 0;

        #endregion

        #region Private-Members

        private AgentMessage _Summary = new AgentMessage { Role = AgentMessageRoleEnum.Summary };
        private List<string> _CoveredMessageIds = new List<string>();

        #endregion
    }
}
