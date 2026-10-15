namespace Hugo.Server.Settings
{
    using System;

    /// <summary>
    /// Agent loop defaults (used from Phase 6).
    /// </summary>
    public class AgentSettings
    {
        #region Public-Members

        /// <summary>
        /// Default maximum loop iterations per turn. Minimum 1, maximum 200, default 12.
        /// </summary>
        public int DefaultMaxIterations
        {
            get
            {
                return _DefaultMaxIterations;
            }
            set
            {
                _DefaultMaxIterations = Math.Clamp(value, 1, 200);
            }
        }

        /// <summary>
        /// Default maximum tool calls per turn. Minimum 1, maximum 500, default 24.
        /// </summary>
        public int DefaultMaxToolCallsPerTurn
        {
            get
            {
                return _DefaultMaxToolCallsPerTurn;
            }
            set
            {
                _DefaultMaxToolCallsPerTurn = Math.Clamp(value, 1, 500);
            }
        }

        /// <summary>
        /// Maximum tool calls run in parallel. Minimum 1, maximum 64, default 4.
        /// </summary>
        public int MaxParallelToolCalls
        {
            get
            {
                return _MaxParallelToolCalls;
            }
            set
            {
                _MaxParallelToolCalls = Math.Clamp(value, 1, 64);
            }
        }

        /// <summary>
        /// Tool count above which tools are retrieved by relevance instead of all being listed. Minimum 1, maximum 10000, default 40.
        /// </summary>
        public int ToolRetrievalThreshold
        {
            get
            {
                return _ToolRetrievalThreshold;
            }
            set
            {
                _ToolRetrievalThreshold = Math.Clamp(value, 1, 10000);
            }
        }

        /// <summary>
        /// Fraction of the context window at which history is compacted. Minimum 0.1, maximum 0.99, default 0.75.
        /// </summary>
        public double CompactionThresholdRatio
        {
            get
            {
                return _CompactionThresholdRatio;
            }
            set
            {
                _CompactionThresholdRatio = Math.Clamp(value, 0.1, 0.99);
            }
        }

        #endregion

        #region Private-Members

        private int _DefaultMaxIterations = 12;
        private int _DefaultMaxToolCallsPerTurn = 24;
        private int _MaxParallelToolCalls = 4;
        private int _ToolRetrievalThreshold = 40;
        private double _CompactionThresholdRatio = 0.75;

        #endregion
    }
}
