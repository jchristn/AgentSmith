namespace Hugo.Server.Protocols
{
    /// <summary>
    /// Arguments naming one agent profile (management tools).
    /// </summary>
    public class McpProfileArguments
    {
        /// <summary>
        /// Profile name or id.
        /// </summary>
        public string? Profile { get; set; } = null;
    }
}
