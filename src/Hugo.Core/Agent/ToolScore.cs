namespace Hugo.Core.Agent
{
    using System;

    /// <summary>
    /// A tool and its similarity to the query, used while ranking in <see cref="ToolRetriever"/>.
    /// </summary>
    public class ToolScore
    {
        /// <summary>
        /// Tool.
        /// </summary>
        public AgentToolDefinition Tool { get; }

        /// <summary>
        /// Cosine similarity.
        /// </summary>
        public double Score { get; }

        /// <summary>
        /// Position in the catalog, the tie breaker.
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="tool">Tool.</param>
        /// <param name="score">Score.</param>
        /// <param name="order">Catalog position.</param>
        /// <exception cref="ArgumentNullException">Thrown when tool is null.</exception>
        public ToolScore(AgentToolDefinition tool, double score, int order)
        {
            Tool = tool ?? throw new ArgumentNullException(nameof(tool));
            Score = score;
            Order = order;
        }
    }
}
