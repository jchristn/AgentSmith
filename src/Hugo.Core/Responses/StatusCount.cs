namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Count of records in one status.
    /// </summary>
    public class StatusCount
    {
        /// <summary>
        /// Status name.
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Count.
        /// </summary>
        public long Count { get; set; } = 0;
    }
}
