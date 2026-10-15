namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using PolyPrompt.Models;

    /// <summary>
    /// Rough token estimate (characters / 4 plus per-message overhead), used to decide compaction before a
    /// provider reports real usage. Deliberately conservative. Thread-safe (stateless).
    /// </summary>
    public static class TokenEstimator
    {
        /// <summary>
        /// Estimate tokens of a string.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Tokens.</returns>
        public static long Estimate(string? text)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            return (text.Length + 3) / 4;
        }

        /// <summary>
        /// Estimate tokens of a message list.
        /// </summary>
        /// <param name="messages">Messages.</param>
        /// <returns>Tokens.</returns>
        public static long Estimate(IEnumerable<ChatMessage> messages)
        {
            long total = 0;
            if (messages == null) return 0;
            foreach (ChatMessage m in messages)
            {
                total += 4 + Estimate(m.Content);
                if (m.ToolCalls != null)
                {
                    foreach (ToolCall c in m.ToolCalls) total += 4 + Estimate(c.Name) + Estimate(c.ArgumentsJson);
                }
            }
            return total;
        }

        /// <summary>
        /// Estimate tokens of tool definitions.
        /// </summary>
        /// <param name="tools">Tools.</param>
        /// <returns>Tokens.</returns>
        public static long Estimate(IEnumerable<AgentToolDefinition> tools)
        {
            long total = 0;
            if (tools == null) return 0;
            foreach (AgentToolDefinition t in tools) total += 8 + Estimate(t.Name) + Estimate(t.Description) + Estimate(t.InputSchemaJson);
            return total;
        }

        /// <summary>
        /// Estimate tokens of a persisted message.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <returns>Tokens.</returns>
        public static long Estimate(AgentMessage message)
        {
            if (message == null) return 0;
            long total = 4 + Estimate(message.Content);
            foreach (AgentMessageToolCall c in message.ToolCalls) total += 4 + Estimate(c.Name) + Estimate(c.ArgumentsJson);
            return total;
        }
    }
}
