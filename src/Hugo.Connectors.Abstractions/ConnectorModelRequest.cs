namespace Hugo.Connectors.Abstractions
{
    using System;

    /// <summary>
    /// A single-turn completion request a connector makes through the server's model gateway.
    /// </summary>
    public class ConnectorModelRequest
    {
        /// <summary>
        /// System prompt.
        /// </summary>
        public string SystemPrompt { get; set; } = String.Empty;

        /// <summary>
        /// User prompt.
        /// </summary>
        public string UserPrompt { get; set; } = String.Empty;

        /// <summary>
        /// Maximum output tokens. Minimum 16, maximum 32768, default 2048.
        /// </summary>
        public int MaxOutputTokens
        {
            get
            {
                return _MaxOutputTokens;
            }
            set
            {
                _MaxOutputTokens = Math.Clamp(value, 16, 32768);
            }
        }

        /// <summary>
        /// Sampling temperature. Minimum 0, maximum 2, default 0 (deterministic, preferred for SQL and context).
        /// </summary>
        public double Temperature
        {
            get
            {
                return _Temperature;
            }
            set
            {
                _Temperature = Math.Clamp(value, 0, 2);
            }
        }

        /// <summary>
        /// Short label for telemetry and audit (for example <c>sql.generate-table-context</c>, <c>sql.nl2sql</c>).
        /// </summary>
        public string Purpose { get; set; } = String.Empty;

        private int _MaxOutputTokens = 2048;
        private double _Temperature = 0;
    }
}
