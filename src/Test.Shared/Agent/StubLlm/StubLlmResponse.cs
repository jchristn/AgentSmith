namespace Test.Shared.Agent.StubLlm
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// One scripted response of the stub LLM: a text message, one or more tool calls (optionally with leading
    /// text), or an HTTP error (optionally with Retry-After). Latency and stream pacing are per response.
    /// </summary>
    public class StubLlmResponse
    {
        #region Public-Members

        /// <summary>
        /// Assistant text. May contain <c>&lt;think&gt;</c> blocks. Null for none.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Reasoning sent in the separate <c>reasoning_content</c> field. Null for none.
        /// </summary>
        public string? Reasoning { get; set; } = null;

        /// <summary>
        /// Tool calls. Never null.
        /// </summary>
        public List<StubToolCall> ToolCalls { get; set; } = new List<StubToolCall>();

        /// <summary>
        /// HTTP status. 200 sends a completion; anything else sends an OpenAI-style error body. Default 200.
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Retry-After header in seconds for error responses. Null for none.
        /// </summary>
        public int? RetryAfterSeconds { get; set; } = null;

        /// <summary>
        /// Error message for error responses.
        /// </summary>
        public string ErrorMessage { get; set; } = "stub error";

        /// <summary>
        /// Milliseconds to wait before sending response headers. Default 0.
        /// </summary>
        public int DelayMs { get; set; } = 0;

        /// <summary>
        /// Milliseconds between streamed chunks. Default 0.
        /// </summary>
        public int ChunkDelayMs { get; set; } = 0;

        /// <summary>
        /// Characters per streamed text chunk. Default 6.
        /// </summary>
        public int ChunkSize { get; set; } = 6;

        /// <summary>
        /// Prompt tokens to report. Null computes characters / 4 of the request messages.
        /// </summary>
        public int? PromptTokens { get; set; } = null;

        /// <summary>
        /// Completion tokens to report. Null computes characters / 4 of the response.
        /// </summary>
        public int? CompletionTokens { get; set; } = null;

        /// <summary>
        /// Whether to report usage. Default true.
        /// </summary>
        public bool ReportUsage { get; set; } = true;

        #endregion

        #region Public-Methods

        /// <summary>
        /// A text answer.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Response.</returns>
        public static StubLlmResponse Message(string text)
        {
            return new StubLlmResponse { Text = text };
        }

        /// <summary>
        /// A single tool call.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <param name="argumentsJson">Arguments JSON.</param>
        /// <returns>Response.</returns>
        public static StubLlmResponse ToolCall(string name, string argumentsJson)
        {
            return new StubLlmResponse { ToolCalls = new List<StubToolCall> { new StubToolCall(name, argumentsJson) } };
        }

        /// <summary>
        /// Several tool calls in one response.
        /// </summary>
        /// <param name="calls">Calls.</param>
        /// <returns>Response.</returns>
        public static StubLlmResponse ToolCallList(params StubToolCall[] calls)
        {
            return new StubLlmResponse { ToolCalls = calls.ToList() };
        }

        /// <summary>
        /// An HTTP error.
        /// </summary>
        /// <param name="statusCode">Status.</param>
        /// <param name="retryAfterSeconds">Retry-After seconds, or null.</param>
        /// <returns>Response.</returns>
        public static StubLlmResponse Error(int statusCode, int? retryAfterSeconds = null)
        {
            return new StubLlmResponse { StatusCode = statusCode, RetryAfterSeconds = retryAfterSeconds, ErrorMessage = "stub error " + statusCode };
        }

        /// <summary>
        /// Set the pre-response delay.
        /// </summary>
        /// <param name="delayMs">Milliseconds.</param>
        /// <returns>This response.</returns>
        public StubLlmResponse WithDelay(int delayMs)
        {
            DelayMs = delayMs;
            return this;
        }

        #endregion
    }
}
