namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Database connectivity test result.
    /// </summary>
    public class DatabaseTestResponse
    {
        /// <summary>
        /// Whether the database answered.
        /// </summary>
        public bool Ok { get; set; } = false;

        /// <summary>
        /// Provider.
        /// </summary>
        public string Provider { get; set; } = String.Empty;

        /// <summary>
        /// Detail.
        /// </summary>
        public string? Message { get; set; } = null;
    }
}
