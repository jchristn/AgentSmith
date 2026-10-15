namespace Hugo.Server.Services
{
    using System;

    /// <summary>
    /// Prometheus GET /api/v1/status/buildinfo response.
    /// </summary>
    public class PrometheusBuildInfo
    {
        /// <summary>
        /// Status, for example success.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Build information, or null when absent.
        /// </summary>
        public BuildInfoVersion? Data { get; set; } = null;
    }
}
