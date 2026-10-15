namespace Hugo.Server.Protocols
{
    /// <summary>
    /// Arguments of the management list tools.
    /// </summary>
    public class McpListArguments
    {
        /// <summary>
        /// Text to match, or null.
        /// </summary>
        public string? Search { get; set; } = null;

        /// <summary>
        /// Most results (1 to 100), or null for 25.
        /// </summary>
        public int? MaxResults { get; set; } = null;

        /// <summary>
        /// Only resources of this connector type (hugo_admin_list_resources), or null.
        /// </summary>
        public string? ConnectorType { get; set; } = null;
    }
}
