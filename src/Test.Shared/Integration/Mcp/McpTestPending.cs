namespace Test.Shared.Integration.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The structured approval_pending result.
    /// </summary>
    public class McpTestPending
    {
        /// <summary>
        /// Status (approval_pending).
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; } = null;

        /// <summary>
        /// Approval identifier.
        /// </summary>
        [JsonPropertyName("approvalId")]
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Summary.
        /// </summary>
        [JsonPropertyName("summary")]
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Next step.
        /// </summary>
        [JsonPropertyName("next")]
        public string? Next { get; set; } = null;
    }
}
