namespace Hugo.Server.Protocols
{
    /// <summary>
    /// Arguments of the hugo_investigate prompt.
    /// </summary>
    public class McpInvestigateArguments
    {
        /// <summary>
        /// The symptom to investigate (for example "checkout is slow").
        /// </summary>
        public string? Symptom { get; set; } = null;
    }
}
