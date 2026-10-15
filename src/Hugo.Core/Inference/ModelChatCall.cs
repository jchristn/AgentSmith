namespace Hugo.Core.Inference
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using PolyPrompt.Models;

    /// <summary>
    /// One chat or tool-chat request to the gateway. Not thread-safe; build one per call.
    /// </summary>
    public class ModelChatCall
    {
        #region Public-Members

        /// <summary>
        /// Primary endpoint. Required.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        public ModelEndpointConfig Endpoint
        {
            get
            {
                return _Endpoint;
            }
            set
            {
                _Endpoint = value ?? throw new ArgumentNullException(nameof(Endpoint));
            }
        }

        /// <summary>
        /// Fallback endpoint used when the primary fails (after retries) or its circuit is open. Null for none.
        /// </summary>
        public ModelEndpointConfig? Fallback { get; set; } = null;

        /// <summary>
        /// Messages, including the system message. Never null; must not be empty when sent.
        /// </summary>
        public List<ChatMessage> Messages
        {
            get
            {
                return _Messages;
            }
            set
            {
                _Messages = value ?? new List<ChatMessage>();
            }
        }

        /// <summary>
        /// Tool definitions. Empty for a plain chat. Ignored when the endpoint does not support tools. Never null.
        /// </summary>
        public List<ToolDefinition> Tools
        {
            get
            {
                return _Tools;
            }
            set
            {
                _Tools = value ?? new List<ToolDefinition>();
            }
        }

        /// <summary>
        /// Tool choice: <c>auto</c>, <c>none</c>, or a provider-specific value. Null sends no tool choice. Default null,
        /// which the gateway turns into <c>auto</c> when tools are present.
        /// </summary>
        public string? ToolChoice { get; set; } = null;

        /// <summary>
        /// Whether to stream when the endpoint supports it. Default true.
        /// </summary>
        public bool Stream { get; set; } = true;

        /// <summary>
        /// Output token cap override. Null uses the endpoint's <see cref="ModelEndpointConfig.MaxOutputTokens"/>.
        /// </summary>
        public int? MaxOutputTokens { get; set; } = null;

        /// <summary>
        /// Temperature override. Null uses the endpoint's temperature.
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Called for each streamed chunk that carries text, reasoning, or tool-call deltas, in order. For
        /// non-streaming calls it is called once with the full text and reasoning. Retries and fallback happen only
        /// before the first chunk is delivered. Null for none.
        /// </summary>
        public Func<ToolChatStreamingChunk, CancellationToken, Task>? OnChunk { get; set; } = null;

        /// <summary>
        /// Telemetry kind label. Default <c>chat</c>.
        /// </summary>
        public string Kind { get; set; } = "chat";

        #endregion

        #region Private-Members

        private ModelEndpointConfig _Endpoint = new ModelEndpointConfig();
        private List<ChatMessage> _Messages = new List<ChatMessage>();
        private List<ToolDefinition> _Tools = new List<ToolDefinition>();

        #endregion
    }
}
