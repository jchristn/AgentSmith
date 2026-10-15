namespace Hugo.Server.Protocols
{
    /// <summary>
    /// Arguments naming one resource (hugo_describe_resource: a bound alias; hugo_admin_describe_resource: a name or id).
    /// </summary>
    public class McpResourceArguments
    {
        /// <summary>
        /// Resource alias, name, or id.
        /// </summary>
        public string? Resource { get; set; } = null;
    }
}
