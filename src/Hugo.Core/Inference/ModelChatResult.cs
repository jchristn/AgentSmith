namespace Hugo.Core.Inference
{
    using System;
    using System.Collections.Generic;
    using PolyPrompt.Models;

    /// <summary>
    /// Aggregated result of a gateway chat call.
    /// </summary>
    public class ModelChatResult
    {
        #region Public-Members

        /// <summary>
        /// Full response text, including any inline think blocks. Never null.
        /// </summary>
        public string Text
        {
            get
            {
                return _Text;
            }
            set
            {
                _Text = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Reasoning reported in a separate field by the provider, if any.
        /// </summary>
        public string? Reasoning { get; set; } = null;

        /// <summary>
        /// Tool calls requested by the model. Never null.
        /// </summary>
        public List<ToolCall> ToolCalls
        {
            get
            {
                return _ToolCalls;
            }
            set
            {
                _ToolCalls = value ?? new List<ToolCall>();
            }
        }

        /// <summary>
        /// Provider finish reason, if reported.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Input (prompt) tokens.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Output (completion) tokens.
        /// </summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>
        /// Whether token counts were estimated (characters / 4) because the provider reported no usage.
        /// </summary>
        public bool TokensEstimated { get; set; } = false;

        /// <summary>
        /// Milliseconds from the start of the successful attempt to the first content chunk; -1 when none.
        /// </summary>
        public long TimeToFirstTokenMs { get; set; } = -1;

        /// <summary>
        /// Milliseconds for the whole gateway call, including retries and fallback.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Key of the endpoint that answered.
        /// </summary>
        public string EndpointKey { get; set; } = String.Empty;

        /// <summary>
        /// Provider that answered.
        /// </summary>
        public ModelProviderEnum Provider { get; set; } = ModelProviderEnum.OpenAICompatible;

        /// <summary>
        /// Model that answered.
        /// </summary>
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// Whether the fallback endpoint answered.
        /// </summary>
        public bool UsedFallback { get; set; } = false;

        /// <summary>
        /// Attempts made in total across endpoints.
        /// </summary>
        public int Attempts { get; set; } = 0;

        #endregion

        #region Private-Members

        private string _Text = String.Empty;
        private List<ToolCall> _ToolCalls = new List<ToolCall>();

        #endregion
    }
}
