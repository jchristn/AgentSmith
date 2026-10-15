namespace Hugo.Connectors.Observability
{
    /// <summary>
    /// Data of <c>/api/v1/status/buildinfo</c>.
    /// </summary>
    public class PromBuildInfo
    {
        /// <summary>
        /// Prometheus version.
        /// </summary>
        public string? Version { get; set; } = null;

        /// <summary>
        /// Git revision.
        /// </summary>
        public string? Revision { get; set; } = null;

        /// <summary>
        /// Go version.
        /// </summary>
        public string? GoVersion { get; set; } = null;
    }
}
