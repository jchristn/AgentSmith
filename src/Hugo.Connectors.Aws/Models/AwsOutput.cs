namespace Hugo.Connectors.Aws.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Structured result of an <c>aws_*</c> tool, serialized to <see cref="Hugo.Connectors.Abstractions.ToolResult.StructuredJson"/>.
    /// Tabular tools fill <see cref="Columns"/> and <see cref="Items"/>; single-object tools fill <see cref="Details"/>.
    /// </summary>
    public class AwsOutput
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public string Tool { get; set; } = String.Empty;

        /// <summary>
        /// Account ID when known.
        /// </summary>
        public string? Account { get; set; } = null;

        /// <summary>
        /// Region.
        /// </summary>
        public string Region { get; set; } = String.Empty;

        /// <summary>
        /// Column names of <see cref="Items"/>, in display order.
        /// </summary>
        public List<string>? Columns { get; set; } = null;

        /// <summary>
        /// Rows, each keyed by column name.
        /// </summary>
        public List<Dictionary<string, string?>>? Items { get; set; } = null;

        /// <summary>
        /// Number of items returned.
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// Token for the next page, or null when there are no more results.
        /// </summary>
        public string? NextToken { get; set; } = null;

        /// <summary>
        /// Whether rows or text were cut to the output limits.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Tool-specific scalar values (status, identifiers, counts).
        /// </summary>
        public Dictionary<string, string?>? Details { get; set; } = null;

        /// <summary>
        /// Notes for the caller (paging hints, warnings).
        /// </summary>
        public List<string>? Notes { get; set; } = null;

        #endregion
    }
}
