namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Health check result.
    /// </summary>
    public class HealthResponse
    {
        /// <summary>
        /// Readiness: healthy, degraded (the database is unreachable), migrating (the database schema is older than this
        /// build expects, for example while another replica applies migrations), starting (the replica is still
        /// migrating or seeding), or draining (the replica is shutting down). Only healthy answers 200. Liveness
        /// answers alive, starting, or draining, always with 200.
        /// </summary>
        public string Status { get; set; } = "healthy";

        /// <summary>
        /// Node identifier.
        /// </summary>
        public string Node { get; set; } = String.Empty;

        /// <summary>
        /// Product version.
        /// </summary>
        public string Version { get; set; } = String.Empty;

        /// <summary>
        /// Database status: ok, unreachable, or unchecked (liveness and startup answers).
        /// </summary>
        public string Database { get; set; } = "ok";

        /// <summary>
        /// Highest migration applied to the database, or 0 when it could not be read.
        /// </summary>
        public int SchemaVersion { get; set; } = 0;

        /// <summary>
        /// Migration version this build requires.
        /// </summary>
        public int ExpectedSchemaVersion { get; set; } = 0;

        /// <summary>
        /// Background services: ok, stalled (the retention loop missed several intervals), or unknown.
        /// </summary>
        public string Background { get; set; } = "unknown";

        /// <summary>
        /// Server time (UTC).
        /// </summary>
        public DateTime Utc { get; set; } = DateTime.UtcNow;
    }
}
